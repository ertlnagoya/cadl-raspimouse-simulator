package main

import (
	"encoding/json"
	"flag"
	"fmt"
	"image/color"
	"os"
	"strconv"
	"strings"
	"sync"
	"time"

	"sort"

	"github.com/ertlnagoya/controller/graph"
	"github.com/ertlnagoya/controller/state"
	"github.com/nats-io/nats.go"
	"gonum.org/v1/plot"
	"gonum.org/v1/plot/plotter"
	"gonum.org/v1/plot/vg"
)

// NatsSubjects holds the NATS subject names loaded from CADL config.
type NatsSubjects struct {
	Init            string `json:"init"`
	Next            string `json:"next"`
	Ret             string `json:"ret"`
	Fin             string `json:"fin"`
	Disp            string `json:"disp"`
	Resource        string `json:"resource"`
	Goalcount       string `json:"goalcount"`
	Stop            string `json:"stop"`
	// Task-arbitration subjects
	DeliveryRequest string `json:"delivery_request"`
	GoalClaim       string `json:"goal_claim"`
	GoalRequest     string `json:"goal_request"`
	TaskEnd         string `json:"task_end"`
	TaskEndResult   string `json:"task_end_result"`
}

// DefaultNatsSubjects returns the default NATS subject names.
func DefaultNatsSubjects() NatsSubjects {
	return NatsSubjects{
		Init: "init", Next: "next", Ret: "ret", Fin: "fin",
		Disp: "disp", Resource: "resource", Goalcount: "goalcount", Stop: "stop",
		DeliveryRequest: "Delivery_Request",
		GoalClaim:       "goal_claim",
		GoalRequest:     "goal_request",
		TaskEnd:         "end",
		TaskEndResult:   "end_result",
	}
}

// cadlCommunicationSetup is the subset of CADL config we need.
type cadlCommunicationSetup struct {
	NatsSubjects NatsSubjects `json:"nats_subjects"`
}
type cadlEnvironment struct {
	NatsURL string `json:"nats_url"`
}
type cadlSimulatorConfig struct {
	Environment cadlEnvironment `json:"environment"`
}
type cadlAgentTemplate struct {
	TemplateId string `json:"templateId"`
	Count      int    `json:"count"`
}
type cadlTaskArbitration struct {
	Enabled                bool    `json:"enabled"`
	Protocol               string  `json:"protocol"`
	MaxClaimDelaySec       float64 `json:"maxClaimDelaySec"`
	DeliveryIntervalSec    float64 `json:"deliveryIntervalSec"`
	GoalSequence           []int   `json:"goalSequence"`
	StartupDelaySec        float64 `json:"startupDelaySec"`
	Parallel               bool    `json:"parallel"` // true = parallel dispatch (matches original thesis)
	DeadlockRecoveryEnabled bool   `json:"deadlockRecoveryEnabled"` // D-3 claim timeout on/off
	DeadlockDetectionSec   float64 `json:"deadlockDetectionSec"`    // unused by arbitrator (Unity only)
}
type cadlConfig struct {
	CommunicationSetup cadlCommunicationSetup `json:"communicationSetup"`
	SimulatorConfig    cadlSimulatorConfig     `json:"simulatorConfig"`
	AgentTemplates     []cadlAgentTemplate     `json:"agentTemplates"`
	TaskArbitration    cadlTaskArbitration     `json:"taskArbitration"`
}

const defaultNatsURL = "nats://localhost:4222"
const defaultNumAgents = 5

// CADLSettings holds all values loaded from CADL config.
type CADLSettings struct {
	Subjects        NatsSubjects
	NatsURL         string
	NumAgents       int
	TaskArbitration cadlTaskArbitration
}

// LoadCADLConfig reads NATS subjects and nats_url from a CADL config JSON file.
func LoadCADLConfig(path string) CADLSettings {
	settings := CADLSettings{
		Subjects:  DefaultNatsSubjects(),
		NatsURL:   defaultNatsURL,
		NumAgents: defaultNumAgents,
	}
	if path == "" {
		return settings
	}
	data, err := os.ReadFile(path)
	if err != nil {
		fmt.Printf("[Config] Could not read %s: %v (using defaults)\n", path, err)
		return settings
	}
	var cfg cadlConfig
	if err := json.Unmarshal(data, &cfg); err != nil {
		fmt.Printf("[Config] Could not parse %s: %v (using defaults)\n", path, err)
		return settings
	}
	// NATS URL
	if cfg.SimulatorConfig.Environment.NatsURL != "" {
		settings.NatsURL = cfg.SimulatorConfig.Environment.NatsURL
	}
	// Agent count from agentTemplates (source of truth: templateId="ROBOT")
	for _, t := range cfg.AgentTemplates {
		if t.TemplateId == "ROBOT" && t.Count > 0 {
			settings.NumAgents = t.Count
			break
		}
	}
	// NATS subjects — only override non-empty values
	s := cfg.CommunicationSetup.NatsSubjects
	if s.Init != "" { settings.Subjects.Init = s.Init }
	if s.Next != "" { settings.Subjects.Next = s.Next }
	if s.Ret != "" { settings.Subjects.Ret = s.Ret }
	if s.Fin != "" { settings.Subjects.Fin = s.Fin }
	if s.Disp != "" { settings.Subjects.Disp = s.Disp }
	if s.Resource != "" { settings.Subjects.Resource = s.Resource }
	if s.Goalcount != "" { settings.Subjects.Goalcount = s.Goalcount }
	if s.Stop != "" { settings.Subjects.Stop = s.Stop }
	settings.TaskArbitration = cfg.TaskArbitration
	fmt.Printf("[Config] Loaded from %s: nats_url=%s numAgents=%d\n", path, settings.NatsURL, settings.NumAgents)
	fmt.Printf("[Config]   init=%s next=%s ret=%s fin=%s disp=%s resource=%s\n",
		settings.Subjects.Init, settings.Subjects.Next, settings.Subjects.Ret,
		settings.Subjects.Fin, settings.Subjects.Disp, settings.Subjects.Resource)
	fmt.Printf("[Config]   taskArbitration.enabled=%v protocol=%s intervalSec=%.1f\n",
		settings.TaskArbitration.Enabled, settings.TaskArbitration.Protocol,
		settings.TaskArbitration.DeliveryIntervalSec)
	return settings
}

// Demand represents a demand message structure.
type Demand struct {
	Id   int  // Demand ID
	Src  int  // Source node
	Dst  int  // Destination node
	Next int  // Next destination node
	Goal int  // Goal node
	Re   bool // Re-request flag
}

type DemandReply struct {
	PermitState int
	ReCount     int // Retry count
}

type DemandResource struct {
	Id int // Demand ID
}

type ReplyResource struct {
	Ok          bool   `json:"ok"`
	EdgeFlags   []bool `json:"edgeFlags"` // フラットなリストに変更
	CrossFlags  []bool `json:"crossFlags"`
	EdgeFlagRow int    `json:"edgeFlagRow"`
}

var RMColor = []string{"Red", "Blue", "Green", "Yellow", "Purple", "Orange", "Pink", "Brown", "Gray", "White"}

// ── Task-arbitration (FCFS delivery system) ───────────────────────────────

// Command sent from arbitrator → winning robot (delivery assignment)
type GoalArbitrator struct {
	Id              int `json:"Id"`
	Goal_arbitrator int `json:"Goal_arbitrator"`
}

// Claim sent from robot → arbitrator ("I want this delivery")
type DemandDelivery struct {
	Id         int `json:"Id"`
	DeliveryID int `json:"DeliveryID"`
}

// Delivery request published by arbitrator → all robots
type DeliveryRequest struct {
	DeliveryID int `json:"DeliveryID"`
	Goal       int `json:"Goal"`
}

// Active delivery being arbitrated
type ActiveDelivery struct {
	DeliveryID      int
	Goal            int
	Claims          []int     // IDs of robots that have claimed (thesis: accumulate all)
	ExpectedRobots  int       // number of idle robots at dispatch time
	DispatchedAt    time.Time // used for claim-round timeout
}

var (
	taskMu         sync.Mutex
	deliveryQueue  []DeliveryRequest
	activeDelivery *ActiveDelivery // single in-flight delivery (both modes)
	deliveryIDCtr  int
	idleRobots     int    // robots not currently assigned a delivery
	simStartedTask bool
	goalSeqIdx     int
	parallelMode   bool   // true = generate goals on-demand, dispatch immediately after claim
	globalGoalSeq  []int  // goal sequence accessible to startNextDelivery()

	// Claim-round timeout: set once during task-arb init
	globalNC                      *nats.Conn
	globalDeliverySubject         string
	globalGoalSubject             string
	globalMaxClaimDelaySec        float64
	globalDeadlockRecoveryEnabled bool // D-3 on/off from CADL taskArbitration.deadlockRecoveryEnabled
)

// Default goal sequence (matches thesis ZIP implementation)
var defaultGoalSeq = []int{9, 8, 3, 2, 5, 10, 7, 8, 0, 4, 6, 1}

func nextGoal(seq []int) int {
	if len(seq) == 0 {
		seq = defaultGoalSeq
	}
	g := seq[goalSeqIdx%len(seq)]
	goalSeqIdx++
	return g
}

// scheduleClaimTimeout sets a deadline for the current claim round.
// Uses globalNC, globalDeliverySubject, globalGoalSubject, globalMaxClaimDelaySec.
// Must be called WITHOUT taskMu held; the AfterFunc goroutine acquires taskMu internally.
//
// Timeout = globalMaxClaimDelaySec * 1.5  (e.g. 7.5s when maxClaimDelaySec=5).
// If the round hasn't fired by then:
//   - Some claims arrived → fire winner with available claims (deadlocked robots excluded)
//   - No claims at all   → re-broadcast so functioning robots can retry after recovery
func scheduleClaimTimeout(deliveryID int) {
	timeout := time.Duration(float64(time.Second) * globalMaxClaimDelaySec * 1.5)
	time.AfterFunc(timeout, func() {
		taskMu.Lock()
		defer taskMu.Unlock()
		if activeDelivery == nil || activeDelivery.DeliveryID != deliveryID {
			return // round already resolved normally
		}
		if len(activeDelivery.Claims) == 0 {
			// No claims at all — all robots may be stuck.
			// Re-broadcast and keep waiting; a robot may recover.
			fmt.Printf("[TaskArb] Claim timeout Delivery_%d: 0/%d claims — re-broadcasting\n",
				deliveryID, activeDelivery.ExpectedRobots)
			b, _ := json.Marshal(DeliveryRequest{
				DeliveryID: activeDelivery.DeliveryID,
				Goal:       activeDelivery.Goal,
			})
			globalNC.Publish(globalDeliverySubject, b)
			// Re-schedule: keep probing until some robot recovers
			go scheduleClaimTimeout(deliveryID)
			return
		}
		// Timeout with some claims: fire with first claimer (deadlock recovery fallback).
		// With immediate-first-claim-wins this path should not be reached in normal
		// operation, but it serves as a safety net for edge cases.
		winner := activeDelivery.Claims[0]
		goal := activeDelivery.Goal
		fmt.Printf("[TaskArb] Claim timeout Delivery_%d: fallback winner=Robot%d\n",
			deliveryID, winner)
		activeDelivery = nil
		idleRobots--
		startNextDelivery(globalNC, globalDeliverySubject)
		assignment := GoalArbitrator{Id: winner, Goal_arbitrator: goal}
		ab, _ := json.Marshal(assignment)
		globalNC.Publish(globalGoalSubject, ab)
	})
}

// startNextDelivery pops the next queued delivery and broadcasts it.
// nc and deliverySubject must be provided by the caller.
// Returns true if a delivery was started.
//
// Parallel mode: generates goals on-demand, allows multiple concurrent
// in-flight deliveries (one per idle robot), matching the original thesis
// implementation where ALL idle robots receive a delivery simultaneously.
//
// Sequential mode: classic single-delivery-at-a-time behaviour (original CADL).
func startNextDelivery(nc *nats.Conn, deliverySubject string) bool {
	if parallelMode {
		// Parallel mode uses "sequential-but-immediate" dispatch:
		//   - Only ONE delivery is in-flight at a time (same activeDelivery gate).
		//   - Goals are generated on-demand (no pre-queued items needed).
		//   - After each claim the next delivery is dispatched immediately without
		//     waiting for the ticker interval, enabling continuous delivery flow.
		// This is compatible with Unity's one-delivery-at-a-time claim model.
		if activeDelivery != nil {
			return false // still waiting for a robot to claim
		}
		if idleRobots == 0 {
			return false
		}
		// Generate next goal on-demand from the global sequence
		deliveryIDCtr++
		g := nextGoal(globalGoalSeq)
		req := DeliveryRequest{DeliveryID: deliveryIDCtr, Goal: g}
		activeDelivery = &ActiveDelivery{
			DeliveryID:     req.DeliveryID,
			Goal:           req.Goal,
			Claims:         []int{},
			ExpectedRobots: idleRobots,
			DispatchedAt:   time.Now(),
		}
		b, _ := json.Marshal(req)
		nc.Publish(deliverySubject, b)
		fmt.Printf("[TaskArb] [Parallel] Broadcast Delivery_%d → goal %d (idleRobots=%d, expect=%d claims)\n",
			req.DeliveryID, req.Goal, idleRobots, idleRobots)
		if globalDeadlockRecoveryEnabled {
			go scheduleClaimTimeout(req.DeliveryID)
		}
		return true
	}

	// Sequential mode (original behaviour — ticker-driven interval)
	if activeDelivery != nil {
		return false // still waiting for claim
	}
	if len(deliveryQueue) == 0 || idleRobots == 0 {
		return false
	}
	req := deliveryQueue[0]
	deliveryQueue = deliveryQueue[1:]
	activeDelivery = &ActiveDelivery{
		DeliveryID:     req.DeliveryID,
		Goal:           req.Goal,
		Claims:         []int{},
		ExpectedRobots: idleRobots,
		DispatchedAt:   time.Now(),
	}
	b, _ := json.Marshal(req)
	nc.Publish(deliverySubject, b)
	fmt.Printf("[TaskArb] Broadcast Delivery_%d → goal %d (idleRobots=%d)\n",
		req.DeliveryID, req.Goal, idleRobots)
	if globalDeadlockRecoveryEnabled {
		go scheduleClaimTimeout(req.DeliveryID)
	}
	return true
}

// MyGraph initializes and returns a predefined graph.
func MyGraph() *graph.Graph {
	g := graph.Graph{}
	g.Init(11, []graph.Path{
		{0, 1, 1},
		{1, 2, 1},
		{2, 3, 2},
		{3, 4, 2.1},
		{4, 5, 1.3},
		{5, 6, 2},
		{6, 7, 1.2},
		{7, 0, 1},
		{7, 8, 2.5},
		{0, 8, 1},
		{1, 8, 1},
		{8, 9, 3},
		{6, 9, 1},
		{5, 9, 0.8},
		{4, 10, 1.4},
		{3, 10, 0.8},
		{2, 10, 1.7},
	})

	return &g
}

// start initializes a NATS subscription and processes messages using the provided routine function.
func start(nc *nats.Conn, name string, routine func(msg *nats.Msg, demand Demand)) {
	sub, err := nc.SubscribeSync(name)
	if err != nil {
		fmt.Printf("Failed to subscribe to %s: %v\n", name, err)
		return
	}
	for {
		msg, err := sub.NextMsg(time.Hour)
		if err != nil {
			fmt.Printf("Error receiving message on %s: %v\n", name, err)
			continue
		}
		var demand Demand
		if err := json.Unmarshal(msg.Data, &demand); err != nil {
			fmt.Printf("Error unmarshaling %s message: %v\n", name, err)
			msg.Respond([]byte(`{"PermitState":-1,"ReCount":0}`))
			continue
		}
		routine(msg, demand)
	}
}

// startResource initializes a NATS subscription for resource queries.
func startResource(nc *nats.Conn, name string, routine func(msg *nats.Msg, demandResource DemandResource)) {
	sub, err := nc.SubscribeSync(name)
	if err != nil {
		fmt.Printf("Failed to subscribe to %s: %v\n", name, err)
		return
	}
	for {
		msg, _ := sub.NextMsg(time.Hour)
		var demandResource DemandResource
		json.Unmarshal(msg.Data, &demandResource)
		routine(msg, demandResource)
	}
}

func print1DArray(arr []bool) {
	// for i, v := range arr {
	// 	fmt.Printf("[%d]: %t\n", i, v)
	// }
	for _, v := range arr {
		fmt.Printf("%t,", v)
	}
	fmt.Println()
}

func print2DArray(arr [][]bool) {
	for i, row := range arr {
		fmt.Printf("Row %d: ", i)
		for j, v := range row {
			fmt.Printf("[%d]: %t ", j, v)
		}
		fmt.Println()
	}
}

// デバッグ出力のため
/*
func flattenAndPrint2DArray(arr [][]bool) {
	count := 0
	for _, row := range arr {
		for _, v := range row {
			fmt.Printf("%t,", v)
			count++
		}
	}
	fmt.Println()
}
*/

var (
	mu     sync.Mutex
	counts = make(map[int]int) // エージェントID → 到達回数
)

// 整数のみのTicksを返すカスタム構造体
type IntegerTicks struct{}

// 整数だけのTicksを返す
func (IntegerTicks) Ticks(min, max float64) []plot.Tick {
	ticks := []plot.Tick{}
	for i := int(min); i <= int(max); i++ {
		ticks = append(ticks, plot.Tick{Value: float64(i), Label: fmt.Sprintf("%d", i)})
	}
	return ticks
}

func drawGraph(filename string) {
	mu.Lock()
	defer mu.Unlock()

	p := plot.New()
	p.Title.Text = "Goal Reached Count"
	p.Y.Label.Text = "Times"
	p.X.Label.Text = "Agent ID"

	// ----- ここを修正：全エージェントIDを指定（例：IDが0〜4まで存在する場合） -----
	totalAgents := numAgents
	keys := make([]int, 0, totalAgents)
	for i := 0; i < totalAgents; i++ {
		keys = append(keys, i)
	}
	// ----------------------------------------------------------------------
	sort.Ints(keys)

	// Y軸を整数表示に固定
	p.Y.Tick.Marker = IntegerTicks{}

	values := make(plotter.Values, len(keys))
	for i, id := range keys {
		// 到達回数がなければ0
		values[i] = float64(counts[id])
	}

	bars, err := plotter.NewBarChart(values, vg.Points(20))
	if err != nil {
		fmt.Println("Bar chart error:", err)
		return
	}
	bars.LineStyle.Width = vg.Length(0)
	bars.Color = color.RGBA{R: 100, G: 150, B: 255, A: 255}

	p.Add(bars)
	p.NominalX(indexToString(keys)...)

	if err := p.Save(8*vg.Inch, 4*vg.Inch, filename); err != nil {
		fmt.Println("Failed to save graph:", err)
		return
	}
	fmt.Println("Graph saved to", filename)
}

func indexToString(keys []int) []string {
	labels := make([]string, len(keys))
	for i, v := range keys {
		labels[i] = fmt.Sprintf("Agent %d", v)
	}
	return labels
}

// numAgents is set from CADL config in main(); used by validId().
var numAgents = defaultNumAgents

func validId(id int) bool {
	return id >= 0 && id < numAgents
}

func main() {
	configPath := flag.String("config", "", "Path to cadl_config.json (optional)")
	flag.Parse()
	if *configPath == "" {
		defaultPath := "../../../unity/Assets/streamingAssets/cadl_config.json"
		if _, err := os.Stat(defaultPath); err == nil {
			*configPath = defaultPath
		}
	}
	cadl := LoadCADLConfig(*configPath)
	sub := cadl.Subjects
	numAgents = cadl.NumAgents

	// Load motivation-sensitive governance config
	motConfig := LoadMotivationConfig(*configPath)

	g := MyGraph()
	table := state.StateTable{}
	table.Init(numAgents)

	nc, err := nats.Connect(cadl.NatsURL)
	if err != nil {
		fmt.Printf("Failed to connect to NATS: %v\n", err)
		return
	}
	m := sync.Mutex{}

	// Handle initialization messages
	go start(nc, sub.Init, func(msg *nats.Msg, demand Demand) {
		println("init_Demand Info: Id =", demand.Id, ", Src =", demand.Src, ", Dst =", demand.Dst, ", Next =", demand.Next)
		m.Lock()
		g.EdgeFlags[demand.Src][demand.Dst] = true
		println(demand.Id)
		table.Demand(demand.Id, demand.Src, demand.Dst)
		m.Unlock()
		// Signal that simulation has started (for task-arbitration ticker)
		taskMu.Lock()
		simStartedTask = true
		taskMu.Unlock()
		var demandReply DemandReply
		demandReply.PermitState = 0 // dummy
		demandReply.ReCount = 0     // dummy
		b, _ := json.Marshal(demandReply)
		msg.Respond(b)
	})

	// Handle finalization messages
	go start(nc, sub.Fin, func(msg *nats.Msg, demand Demand) {
		m.Lock()
		g.SetZero()
		m.Unlock()
		msg.Respond([]byte("1"))
		println("fin")
	})

	// Handle return messages
	go start(nc, sub.Ret, func(msg *nats.Msg, demand Demand) {
		if !validId(demand.Id) {
			msg.Respond([]byte(`{"PermitState":-1,"ReCount":0}`))
			return
		}
		m.Lock()
		crossIdx := table.States[demand.Id].Cross
		if crossIdx >= 0 && crossIdx < g.Size {
			g.CrossFlags[crossIdx] = false
		}
		table.Ret(demand.Id)
		m.Unlock()
		var demandReply DemandReply
		demandReply.PermitState = 0 // dummy
		demandReply.ReCount = 0     // dummy
		b, _ := json.Marshal(demandReply)
		msg.Respond(b)
		println("--------------------------------------------")
		println("ret of " + strconv.Itoa(demand.Id) + " : " + strconv.Itoa(demand.Src))
		table.Output()
	})

	// Handle next node messages
	// [Motivation Extension] For C-SoS, motivation affects the verification
	// response. When an over-budget robot proposes a route, the arbitrator
	// may reject it with a higher retry count, symmetrically to how A-SoS
	// motivation throttling works. This makes motivation a latent variable
	// that both governance types interpret through their own institutional lens.
	go start(nc, sub.Next, func(msg *nats.Msg, demand Demand) {
		println("next_Demand RM:", RMColor[demand.Id], "Src:", demand.Src, "Dst:", demand.Dst, "Next:", demand.Next, "Goal:", demand.Goal)
		m.Lock()
		//for debug
		var next_est = g.DirectionDijkstra(demand.Dst, demand.Goal)
		if next_est != demand.Next {
			fmt.Println("next is different from next_est")
			fmt.Println("next_est: " + strconv.Itoa(next_est))
		}
		fmt.Println("[]CrossFlags:")
		print1DArray(g.CrossFlags)
		fmt.Println("EdgeFlags:")
		print2DArray(g.EdgeFlags)
		//debug end

		// [Motivation] Check C-SoS delivery cap (P1-P4 patterns) and commitment budget
		mu.Lock()
		agentGoals := counts[demand.Id]
		mu.Unlock()

		// C-SoS P1-P4: permanently reject retired agents (cap reached)
		if motConfig.IsRetired(demand.Id, agentGoals) {
			m.Unlock()
			var demandReply DemandReply
			demandReply.PermitState = -1
			demandReply.ReCount = 9999 // large value = effectively retired
			fmt.Printf("[Motivation] C-SoS Agent %d RETIRED (cap=%d, goals=%d)\n",
				demand.Id, motConfig.MaxDeliveries[demand.Id], agentGoals)
			b, _ := json.Marshal(demandReply)
			msg.Respond(b)
			return
		}

		extraWait := motConfig.ComputeExtraWait(demand.Id, agentGoals)
		if extraWait > 0 && !demand.Re {
			// Over-budget in C-SoS: reject the proposal with extra wait
			m.Unlock()
			var demandReply DemandReply
			demandReply.PermitState = -1
			demandReply.ReCount = extraWait + 1
			fmt.Printf("[Motivation] C-SoS Agent %d throttled: goals=%d, budget=%.1f, extra_wait=%d\n",
				demand.Id, agentGoals, motConfig.GetAgentBudget(demand.Id), extraWait)
			b, _ := json.Marshal(demandReply)
			msg.Respond(b)
			return
		}

		var demandReply DemandReply
		demandReply.ReCount = 0
		g.EdgeFlags[table.States[demand.Id].Src][table.States[demand.Id].Dst] = false
		next := demand.Next
		if !g.Available(demand.Dst, next) {
			if !demand.Re && !g.EdgeFlags[next][demand.Dst] {
				g.EdgeFlags[demand.Src][demand.Dst] = true
				table.Demand(demand.Id, demand.Src, demand.Dst)
				m.Unlock()
				c := -int(7*g.Edges[next][demand.Dst]) - 1
				if c >= 0 {
					c = -1
				}
				demandReply.ReCount = -1 * c
				fmt.Println("next_Demand RM:", RMColor[demand.Id], "not available")
				demandReply.PermitState = -1 //資源要求不受理
				b, _ := json.Marshal(demandReply)
				msg.Respond(b)
				return
			}
			fmt.Println("next_Demand RM:", RMColor[demand.Id], "not available and turn back")
			next = demand.Src
			demandReply.PermitState = -2 //資源要求不受理かつ反転
			b, _ := json.Marshal(demandReply)
			msg.Respond(b)
		} else {
			demandReply.PermitState = 1 //資源要求受理
			b, _ := json.Marshal(demandReply)
			msg.Respond(b)
		}
		g.CrossFlags[demand.Dst] = true
		g.EdgeFlags[demand.Dst][next] = true
		table.Demand(demand.Id, demand.Dst, next)
		m.Unlock()

		fmt.Println("next_Demand RM:", RMColor[demand.Id], "available")
		println("--------------------------------------------")
		println("next of " + strconv.Itoa(demand.Id) + " : " + strconv.Itoa(next))
		table.Output()
		g.Output()
	})

	// Handle display messages
	go start(nc, sub.Disp, func(msg *nats.Msg, demand Demand) {
		n := 0
		m.Lock()
		if demand.Dst == -1 {
			if g.CrossFlags[demand.Src] {
				n = 1
			}
		} else {
			if g.EdgeFlags[demand.Src][demand.Dst] || g.EdgeFlags[demand.Dst][demand.Src] {
				n = 1
			}
		}
		m.Unlock()
		msg.Respond([]byte(strconv.Itoa(n)))
	})

	// 到達回数の受信と記録用
	go start(nc, sub.Goalcount, func(msg *nats.Msg, _ Demand) {
		data := string(msg.Data)
		parts := strings.Split(data, ",")
		if len(parts) != 2 {
			println("invalid goalcount message")
			return
		}

		id, err1 := strconv.Atoi(parts[0])
		level, err2 := strconv.Atoi(parts[1])
		if err1 != nil || err2 != nil {
			println("parse error in goalcount message")
			return
		}

		println("Agent", id, "reached goal", level, "times")

		mu.Lock()
		counts[id] = level
		mu.Unlock()

		// [Motivation] Log budget status on goal completion
		if motConfig.Enabled {
			budget := motConfig.GetAgentBudget(id)
			extra := motConfig.ComputeExtraWait(id, level)
			status := "OK"
			if float64(level) > budget {
				status = "OVER-BUDGET"
			}
			fmt.Printf("[Motivation] C-SoS Agent %d goal=%d, m=%.2f, B=%.1f, extra_wait=%d [%s]\n",
				id, level, motConfig.GetMotivation(id), budget, extra, status)
		}
	})

	go start(nc, sub.Stop, func(msg *nats.Msg, _ Demand) {
		data := string(msg.Data)
		parts := strings.Split(data, ",")
		id := parts[0]
		time := parts[1]
		println("Agent", id, "Stop", "because", time, "has passed")
		drawGraph("goalcount.png")
	})

	// リソース要求のハンドリング
	go startResource(nc, sub.Resource, func(msg *nats.Msg, demandResource DemandResource) {
		// println("resource_Demand Info: Id =", demandResource.Id)
		m.Lock()
		//Unityでダイクストラを実行する際、自分の辺は使用していないものとみなす。
		g.EdgeFlags[table.States[demandResource.Id].Src][table.States[demandResource.Id].Dst] = false
		var reply ReplyResource
		reply.Ok = true
		reply.EdgeFlags = g.FlattenEdgeFlags() // フラットなリストを返す
		reply.CrossFlags = g.CrossFlags
		reply.EdgeFlagRow = len(g.EdgeFlags) // 行数を追加
		//Unityへの返信が終わったら、使用中とする。(このコードの位置が適切かは不明)
		g.EdgeFlags[table.States[demandResource.Id].Src][table.States[demandResource.Id].Dst] = true
		m.Unlock()
		b, _ := json.Marshal(reply)
		msg.Respond(b)

		// 配列のデバッグ出力
		// fmt.Println("CrossFlags:")
		// print1DArray(g.CrossFlags)

		// fmt.Println("EdgeFlags:")
		// print2DArray(g.EdgeFlags)
		// fmt.Printf("%t\n", reply.EdgeFlags)
	})

	// ── Task-arbitration (FCFS) ─────────────────────────────────────────────
	if cadl.TaskArbitration.Enabled {
		idleRobots = numAgents
		goalSeq := cadl.TaskArbitration.GoalSequence
		if len(goalSeq) == 0 {
			goalSeq = defaultGoalSeq
		}
		globalGoalSeq = goalSeq  // make accessible to startNextDelivery()
		parallelMode = cadl.TaskArbitration.Parallel
		intervalSec := cadl.TaskArbitration.DeliveryIntervalSec
		if intervalSec <= 0 {
			intervalSec = 5.0
		}
		// Globals for claim-round timeout (scheduleClaimTimeout)
		globalNC = nc
		globalDeliverySubject = sub.DeliveryRequest
		globalGoalSubject = sub.GoalRequest
		globalMaxClaimDelaySec = cadl.TaskArbitration.MaxClaimDelaySec
		if globalMaxClaimDelaySec <= 0 {
			globalMaxClaimDelaySec = 5.0
		}
		globalDeadlockRecoveryEnabled = cadl.TaskArbitration.DeadlockRecoveryEnabled
		fmt.Printf("[TaskArb] mode=%s intervalSec=%.1f startupDelay=%.1f deadlockRecovery=%v claimTimeout=%.1fs\n",
			map[bool]string{true: "parallel", false: "sequential"}[parallelMode],
			intervalSec, cadl.TaskArbitration.StartupDelaySec,
			globalDeadlockRecoveryEnabled, globalMaxClaimDelaySec*1.5)

		// goal_claim: original thesis immediate-first-claim-wins mechanism.
		// The first robot whose claim arrives wins immediately — no waiting for
		// other robots.  The claimDelay (agentMotivation) creates the priority
		// ordering: high-motivation robots wait less and therefore claim sooner.
		// This matches the original thesis d287121 implementation.
		go func() {
			claimSub, _ := nc.SubscribeSync(sub.GoalClaim)
			for {
				msg, err := claimSub.NextMsg(time.Hour)
				if err != nil {
					fmt.Printf("[TaskArb] goal_claim error: %v\n", err)
					continue
				}
				var claim DemandDelivery
				if err := json.Unmarshal(msg.Data, &claim); err != nil {
					msg.Respond([]byte("0"))
					continue
				}
				taskMu.Lock()
				// Stale or no active delivery — reject claim
				if activeDelivery == nil || claim.DeliveryID != activeDelivery.DeliveryID {
					taskMu.Unlock()
					msg.Respond([]byte("0"))
					continue
				}
				// First claim wins immediately (original thesis behaviour)
				winner := claim.Id
				goal := activeDelivery.Goal
				fmt.Printf("[TaskArb] Robot%d claimed Delivery_%d — first-claim-wins\n",
					claim.Id, claim.DeliveryID)
				activeDelivery = nil
				idleRobots--
				startNextDelivery(nc, sub.DeliveryRequest)
				taskMu.Unlock()

				msg.Respond([]byte("1")) // winner acknowledged
				assignment := GoalArbitrator{Id: winner, Goal_arbitrator: goal}
				ab, _ := json.Marshal(assignment)
				nc.Publish(sub.GoalRequest, ab)
				fmt.Printf("[TaskArb] FCFS winner=Robot%d goal=%d (idleRobots=%d)\n",
					winner, goal, idleRobots)
			}
		}()

		// task_end: robot finished delivery → mark as idle, dispatch next if no active delivery.
		// Thesis: on task end, TotalRobots++ then tryStartNextDelivery() — no re-broadcast.
		go start(nc, sub.TaskEnd, func(msg *nats.Msg, demand Demand) {
			taskMu.Lock()
			idleRobots++
			msg.Respond([]byte(`{"PermitState":0,"ReCount":0}`))
			fmt.Printf("[TaskArb] Robot%d finished delivery (idleRobots=%d)\n",
				demand.Id, idleRobots)
			// Only start a new delivery if there is no active claim round in progress.
			// The returning robot will be included in the next round's ExpectedRobots.
			if activeDelivery == nil {
				startNextDelivery(nc, sub.DeliveryRequest)
			}
			taskMu.Unlock()
		})

		// Delivery ticker
		go func() {
			// Wait until first robot sends init
			for {
				time.Sleep(200 * time.Millisecond)
				taskMu.Lock()
				ready := simStartedTask
				taskMu.Unlock()
				if ready {
					break
				}
			}
			fmt.Println("[TaskArb] Simulation started. Waiting for robots to reach home...")
			startupDelay := cadl.TaskArbitration.StartupDelaySec
			if startupDelay > 0 {
				fmt.Printf("[TaskArb] Startup delay: %.0fs\n", startupDelay)
				time.Sleep(time.Duration(float64(time.Second) * startupDelay))
			}
			fmt.Println("[TaskArb] Generating delivery tasks.")

			if parallelMode {
				// Parallel mode: kick off the first delivery immediately,
				// then use a 1s ticker as a safety net (catches edge cases
				// where a delivery goes unclaimed, e.g. all robots are mid-
				// delivery when a new idle robot appears).
				taskMu.Lock()
				fmt.Printf("[TaskArb] [Parallel] Initial dispatch (idleRobots=%d)\n", idleRobots)
				startNextDelivery(nc, sub.DeliveryRequest)
				taskMu.Unlock()

				ticker := time.NewTicker(time.Second) // 1s safety-net
				defer ticker.Stop()
				for range ticker.C {
					taskMu.Lock()
					if activeDelivery == nil {
						startNextDelivery(nc, sub.DeliveryRequest)
					} else {
						// Re-broadcast current pending delivery so newly idle
						// robots get a chance to compete.
						if idleRobots > 0 {
							b, _ := json.Marshal(DeliveryRequest{
								DeliveryID: activeDelivery.DeliveryID,
								Goal:       activeDelivery.Goal,
							})
							nc.Publish(sub.DeliveryRequest, b)
						}
					}
					taskMu.Unlock()
				}
			} else {
				// Sequential mode: one delivery every intervalSec
				ticker := time.NewTicker(time.Duration(float64(time.Second) * intervalSec))
				defer ticker.Stop()
				for range ticker.C {
					taskMu.Lock()
					deliveryIDCtr++
					g := nextGoal(goalSeq)
					req := DeliveryRequest{DeliveryID: deliveryIDCtr, Goal: g}
					deliveryQueue = append(deliveryQueue, req)
					startNextDelivery(nc, sub.DeliveryRequest)
					taskMu.Unlock()
				}
			}
		}()
	}

	wg := sync.WaitGroup{}
	wg.Add(1)
	wg.Wait()
}
