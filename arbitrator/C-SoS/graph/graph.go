// Package graph provides functionalities for creating and manipulating graphs,
// including edge creation, pathfinding using Dijkstra's algorithm, and various
// utility methods to manage edges and weights.
package graph

import (
	"math/rand"
	"strconv"
)

// Graph represents a graph structure with edges and flags for managing
// availability and weights of the edges.
type Graph struct {
	Size       int         // Number of nodes in the graph
	Edges      [][]float32 // Matrix of edge lengths
	EdgeFlags  [][]bool    // Matrix of edge availability flags
	CrossFlags []bool      // Array of node availability flags
}

// Available checks if an edge between two nodes is available for traversal.
func (g *Graph) Available(src int, dst int) bool {
	return !(g.EdgeFlags[src][dst] || g.EdgeFlags[dst][src] || g.CrossFlags[src])
}

// MakeEdge sets the length of an edge between two nodes.
func (g *Graph) MakeEdge(i int, j int, length float32) {
	g.Edges[i][j] = length
	g.Edges[j][i] = length
}

// SetZero resets all edge and cross flags to false.
func (g *Graph) SetZero() {
	for i := 0; i < g.Size; i++ {
		g.CrossFlags[i] = false
		for j := 0; j < g.Size; j++ {
			g.EdgeFlags[i][j] = false
		}
	}
}

// Path represents a path between two nodes with a specific length.
type Path struct {
	Cross1 int     // Starting node of the path
	Cross2 int     // Ending node of the path
	Length float32 // Length of the path
}

// Init initializes the graph with a specified size and a list of edges.
func (g *Graph) Init(size int, edges []Path) {
	g.Size = size
	g.Edges = make([][]float32, size)

	for i := 0; i < size; i++ {
		g.Edges[i] = make([]float32, size)
		for j := 0; j < size; j++ {
			g.Edges[i][j] = 100000
		}
	}

	g.EdgeFlags = make([][]bool, size)
	for i := 0; i < size; i++ {
		g.EdgeFlags[i] = make([]bool, size)
		for j := 0; j < size; j++ {
			g.EdgeFlags[i][j] = false
		}
	}

	g.CrossFlags = make([]bool, size)
	for i := 0; i < size; i++ {
		g.CrossFlags[i] = false
	}

	for i := 0; i < len(edges); i++ {
		edge := edges[i]
		g.MakeEdge(edge.Cross1, edge.Cross2, edge.Length)
	}
}

// DirectionWeight calculates the directional weight of an edge between two nodes.
func (g *Graph) DirectionWeight(i int, j int) float32 {
	rv := g.Edges[i][j]
	if g.EdgeFlags[i][j] {
		rv += 1.5
	}
	if g.EdgeFlags[j][i] {
		rv += 7
	}
	if g.CrossFlags[j] {
		rv += 1
	}
	return rv
}

// FlagWeight calculates the flag-based weight of an edge between two nodes.
func (g *Graph) FlagWeight(i int, j int) float32 {
	rv := g.Edges[i][j]
	if g.EdgeFlags[i][j] {
		rv += 7
	}
	if g.EdgeFlags[j][i] {
		rv += 7
	}
	if g.CrossFlags[j] {
		rv += 1
	}
	return rv
}

// Length returns the length of an edge between two nodes.
func (g *Graph) Length(i int, j int) float32 {
	return g.Edges[i][j]
}

// NaiveDijkstra finds the shortest path between two nodes using the basic edge lengths.
func (g *Graph) NaiveDijkstra(start int, end int) int {
	return g.Dijkstra(start, end, g.Length)
}

// FlagDijkstra finds the shortest path between two nodes considering flag-based weights.
func (g *Graph) FlagDijkstra(start int, end int) int {
	return g.Dijkstra(start, end, g.FlagWeight)
}

// DirectionDijkstra finds the shortest path between two nodes considering directional weights.
func (g *Graph) DirectionDijkstra(start int, end int) int {
	return g.Dijkstra(start, end, g.DirectionWeight)
}

// FlattenEdgeFlags converts the 2D EdgeFlags matrix to a flat 1D slice for JSON serialisation.
func (g *Graph) FlattenEdgeFlags() []bool {
	n := len(g.EdgeFlags)
	flat := make([]bool, n*n)
	for i := 0; i < n; i++ {
		for j := 0; j < n; j++ {
			flat[i*n+j] = g.EdgeFlags[i][j]
		}
	}
	return flat
}

// RandomDijkstra selects a random adjacent node from the start node.
func (g *Graph) RandomDijkstra(start int, end int) int {
	list := make([]int, 0)
	for i := 0; i < g.Size; i++ {
		if g.Edges[start][i] < 1000 {
			list = append(list, i)
		}
	}

	return list[rand.Intn(len(list))]
}

// Dijkstra finds the shortest path between two nodes using a custom length function.
func (g *Graph) Dijkstra(start int, end int, length func(int, int) float32) int {
	costs := make([]float32, g.Size)
	precs := make([]int, g.Size)
	checks := make([]bool, g.Size)
	for i := 0; i < g.Size; i++ {
		costs[i] = 100000000
		checks[i] = false
	}
	costs[start] = 0

	for l := 0; l < g.Size; l++ {
		min := float32(10000000000)
		i := -1
		for k := 0; k < g.Size; k++ {
			if !checks[k] && costs[k] < min {
				min = costs[k]
				i = k
			}
		}

		checks[i] = true
		for j := 0; j < g.Size; j++ {
			newcost := float32(costs[i]) + length(i, j)
			if newcost < costs[j] {
				costs[j] = newcost
				precs[j] = i
			}
		}
	}

	tmp := end
	for {
		print(strconv.Itoa(tmp) + " <- ")
		if start == precs[tmp] {
			println(start)
			return tmp
		}
		tmp = precs[tmp]
	}
}

// Output prints the edges with active flags.
func (g *Graph) Output() {
	for i := 0; i < g.Size; i++ {
		for j := 0; j < g.Size; j++ {
			if g.EdgeFlags[i][j] {
				println(strconv.Itoa(i) + " -> " + strconv.Itoa(j))
			}
		}
	}
}
