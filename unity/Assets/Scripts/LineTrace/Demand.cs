using System;

namespace LineTrace
{
    // ── Resource request (edge-occupancy fetch before Dijkstra) ──────────────

    /// <summary>Robot → arbitrator: "give me the current edge/cross flags for Dijkstra."</summary>
    [Serializable]
    public struct DemandResource
    {
        public int Id;  // requesting robot ID
    }

    /// <summary>
    /// Arbitrator → robot: current edge/cross occupancy state.
    ///
    /// Field names are lowercase camelCase to match Go arbitrator's json tags exactly:
    ///   json:"ok" / json:"edgeFlags" / json:"crossFlags" / json:"edgeFlagRow"
    /// Unity JsonUtility is case-sensitive; uppercase names would silently fail to
    /// deserialize (Ok=false, EdgeFlags=null) even though the data arrives correctly.
    ///
    /// edgeFlags is flat row-major: edgeFlags[i * edgeFlagRow + j] == EdgeFlags[i][j]
    /// </summary>
    [Serializable]
    public class ReplyResource
    {
        public bool ok;
        public bool[] edgeFlags;   // flat row-major: index = i * edgeFlagRow + j
        public bool[] crossFlags;
        public int edgeFlagRow;    // number of rows/cols (= graph size n)
    }


    // ── Task-arbitration types (FCFS delivery system) ─────────────────────────

    /// <summary>Delivery task broadcast from arbitrator → all robots.</summary>
    [Serializable]
    public class DeliveryRequest
    {
        public int DeliveryID;
        public int Goal;
    }

    /// <summary>Robot → arbitrator: "I want to claim this delivery task."</summary>
    [Serializable]
    public struct Demand_Delivery
    {
        public int Id;
        public int DeliveryID;
    }

    /// <summary>Arbitrator → winner robot: "You are assigned this task."</summary>
    [Serializable]
    public class GoalAssignment
    {
        public int Id;
        public int Goal_arbitrator;
    }

    // ── Core navigation demand types ──────────────────────────────────────────

    [Serializable]
    public struct Demand
    {
        public int Id;  //Demand ID
        public int Src; //Source node
        public int Dst; //Destination node
        public int Goal; //Goal node
        public bool Re; //Re-request flag
    }

    [Serializable]
    public struct Reply
    {
        public bool Ok; // Whether the demand was successful
        public bool[][] EdgeFlags;  // Matrix of edge availability flags
        public bool[] CrossFlags;   // Array of node availability flags
    }

    [Serializable]
    public struct Demand_CSoS
    {
        public int Id;  //Demand ID
        public int Src; //Source node
        public int Dst; //Destination node
        public int Next; //Next destination node
        public int Goal; //Goal node
        public bool Re; //Re-request flag
    }
}