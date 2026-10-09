// Package state provides functionalities for managing state transitions and
// maintaining a state table with demand and return operations.
package state

// Unknown represents an undefined state.
const Unknown = -1

// State represents a state with source, destination, and cross states.
type State struct {
	Src   int // Source state
	Dst   int // Destination state
	Cross int // Cross state
}

// StateTable manages a table of states.
type StateTable struct {
	States []State // Slice of states
}

// Init initializes the state table with a specified size and sets all states to Unknown.
func (t *StateTable) Init(size int) {
	t.States = make([]State, size)
	for i := 0; i < size; i++ {
		t.States[i] = State{Unknown, Unknown, Unknown}
	}
}

// SetZero resets all states in the table to Unknown.
func (t *StateTable) SetZero() {
	for i := 0; i < len(t.States); i++ {
		t.States[i] = State{Unknown, Unknown, Unknown}
	}
}

// Demand sets the source and destination of a state and updates the cross state to source.
func (t *StateTable) Demand(id int, src int, dst int) {
	t.States[id] = State{src, dst, src}
}

// Ret resets the cross state of a specified state to Unknown.
func (t *StateTable) Ret(id int) {
	t.States[id].Cross = Unknown
}

// Output prints the current states in the table, indicating cross state with "^" or "*".
func (t *StateTable) Output() {
	for i := 0; i < len(t.States); i++ {
		state := t.States[i]
		print(i)
		print(" : ")
		if state.Cross == Unknown {
			print("^")
		} else {
			print("*")
		}
		print(state.Src)
		print(" -> ")
		println(state.Dst)
	}
}
