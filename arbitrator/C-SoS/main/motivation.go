package main

// ──────────────────────────────────────────────────────────────────────
// motivation.go — Motivation-Sensitive Governance Extension (D-SoS model)
//
// Loads motivationConfig from cadl_config.json and provides a budget-
// based throttling mechanism. When a robot's cumulative goal count
// exceeds its individual budget B_i, the arbitrator injects extra
// retry ticks proportional to ρ * overshoot.
//
// Budget formula: B_i = BudgetBase + Kappa * m_i
// Extra wait:     floor(Rho * max(0, used_i - B_i) * WaitScale)
//
// This file is a drop-in addition to D-SoS main.go.
// No changes to existing code are required — just call
// LoadMotivationConfig() and ComputeExtraWait() from main.go.
// ──────────────────────────────────────────────────────────────────────

import (
	"encoding/json"
	"fmt"
	"math"
	"os"
)

// MotivationConfig holds the motivation-sensitive governance parameters
// loaded from the "motivationConfig" section of cadl_config.json.
type MotivationConfig struct {
	Enabled         bool      `json:"enabled"`
	Model           string    `json:"model"`            // "none", "commitment_budget", "hybrid"
	Rho             float64   `json:"rho"`              // Sensitivity to budget overshoot [0, 1]
	Kappa           float64   `json:"kappa"`            // Budget extension factor
	BudgetBase      int       `json:"budgetBase"`       // Base commitment budget
	WaitScale       float64   `json:"waitScale"`        // Overshoot-to-retry conversion
	AgentMotivation []float64 `json:"agentMotivation"`  // Per-agent motivation values
	Profile         string    `json:"profile"`          // Profile name (for logging)
	MaxDeliveries   []int     `json:"maxDeliveries"`    // C-SoS P1-P4: per-robot delivery cap (0/nil = no cap)
}

// cadlFullConfig is the extended CADL config struct that includes motivationConfig.
type cadlFullConfig struct {
	CommunicationSetup cadlCommunicationSetup `json:"communicationSetup"`
	SimulatorConfig    cadlSimulatorConfig     `json:"simulatorConfig"`
	AgentTemplates     []cadlAgentTemplate     `json:"agentTemplates"`
	MotivationConfig   MotivationConfig        `json:"motivationConfig"`
}

// DefaultMotivationConfig returns a disabled motivation config (baseline behavior).
func DefaultMotivationConfig() MotivationConfig {
	return MotivationConfig{
		Enabled:    false,
		Model:      "none",
		Rho:        0.0,
		Kappa:      0.0,
		BudgetBase: 0,
		WaitScale:  0.0,
	}
}

// LoadMotivationConfig reads the motivationConfig section from cadl_config.json.
// Returns DefaultMotivationConfig() if the file is missing or doesn't have the section.
func LoadMotivationConfig(path string) MotivationConfig {
	mc := DefaultMotivationConfig()
	if path == "" {
		return mc
	}

	data, err := os.ReadFile(path)
	if err != nil {
		return mc
	}

	var cfg cadlFullConfig
	if err := json.Unmarshal(data, &cfg); err != nil {
		return mc
	}

	mc = cfg.MotivationConfig

	if mc.Enabled {
		fmt.Printf("[Motivation] Loaded: model=%s, rho=%.2f, kappa=%.1f, budget_base=%d, wait_scale=%.1f\n",
			mc.Model, mc.Rho, mc.Kappa, mc.BudgetBase, mc.WaitScale)
		fmt.Printf("[Motivation] Profile: %s, agents: %v\n", mc.Profile, mc.AgentMotivation)
	} else {
		fmt.Println("[Motivation] Disabled (baseline behavior)")
	}

	return mc
}

// GetAgentBudget computes the commitment budget for agent i.
// B_i = BudgetBase + Kappa * m_i
func (mc *MotivationConfig) GetAgentBudget(agentId int) float64 {
	if !mc.Enabled || agentId < 0 || agentId >= len(mc.AgentMotivation) {
		return math.MaxFloat64 // No budget limit
	}
	m_i := mc.AgentMotivation[agentId]
	return float64(mc.BudgetBase) + mc.Kappa*m_i
}

// ComputeExtraWait calculates the extra retry ticks for an agent
// based on its cumulative goal count vs budget.
//
// Returns 0 if motivation is disabled or the agent is under budget.
// Returns floor(Rho * max(0, used - B_i) * WaitScale) otherwise.
//
// The returned value is added to the normal retry count, making
// over-budget robots wait longer before their next routing attempt.
func (mc *MotivationConfig) ComputeExtraWait(agentId int, cumulativeGoals int) int {
	if !mc.Enabled || mc.Rho == 0.0 {
		return 0
	}

	budget := mc.GetAgentBudget(agentId)
	overshoot := float64(cumulativeGoals) - budget

	if overshoot <= 0 {
		return 0
	}

	extraWait := int(math.Floor(mc.Rho * overshoot * mc.WaitScale))
	if extraWait < 0 {
		extraWait = 0
	}

	return extraWait
}

// IsRetired returns true when the agent has reached its C-SoS delivery cap.
// Returns false if no cap is configured (standard C-SoS Reference behavior).
func (mc *MotivationConfig) IsRetired(agentId int, cumulativeGoals int) bool {
	if len(mc.MaxDeliveries) == 0 {
		return false // No cap configured
	}
	if agentId < 0 || agentId >= len(mc.MaxDeliveries) {
		return false
	}
	cap := mc.MaxDeliveries[agentId]
	if cap <= 0 {
		return false // 0 means no cap
	}
	return cumulativeGoals >= cap
}

// GetMotivation returns the motivation value for a given agent.
// Returns 0.5 (neutral) if not configured.
func (mc *MotivationConfig) GetMotivation(agentId int) float64 {
	if agentId < 0 || agentId >= len(mc.AgentMotivation) {
		return 0.5
	}
	return mc.AgentMotivation[agentId]
}

// LogBudgetStatus prints the budget status for all agents.
// Call this periodically or on goal completion for debugging.
func (mc *MotivationConfig) LogBudgetStatus(goalCounts map[int]int) {
	if !mc.Enabled {
		return
	}
	fmt.Println("[Motivation] Budget Status:")
	for i := 0; i < len(mc.AgentMotivation); i++ {
		budget := mc.GetAgentBudget(i)
		used := goalCounts[i]
		extra := mc.ComputeExtraWait(i, used)
		status := "OK"
		if float64(used) > budget {
			status = "OVER"
		}
		fmt.Printf("  Agent %d: m=%.2f, B=%.1f, used=%d, extra_wait=%d [%s]\n",
			i, mc.AgentMotivation[i], budget, used, extra, status)
	}
}
