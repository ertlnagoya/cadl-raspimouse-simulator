using CADLConfig;
using RasPiMouse;
using UnityEngine;

namespace LineTrace.Handlers
{
    public enum CarAction
    {
        Go,
        Back,
        Cross,
        Wait    // Waiting for LLM instruction after collision
    }

    public class Handler
    {
        private Mouse mouse;
        private CarAction action;

        private GoHandler goHandler;
        private CrossHandler crossHandler;
        private BackHandler backHandler;

        private float t;
        private float maxT;

        private bool isCollision = false;

        private string RMColor;

        // Flag: true when back action completed after collision (for Pilot_MCP/Pilot_CSoS to check)
        public bool CollisionBackCompleted { get; set; } = false;

        // Flag: true when reversing for set_path (not collision), should Go after reverse
        private bool isReversingForPath = false;

        // Flag: MCP mode (wait for LLM after collision) vs original D-SoS/C-SoS mode
        private bool isMCPMode = false;

        public Handler(Mouse mouse1, string color, bool mcpMode = false)
        {
            action = CarAction.Go;
            mouse = mouse1;
            goHandler = new GoHandler(mouse1);
            backHandler = new BackHandler(mouse1);
            crossHandler = new CrossHandler(mouse1);

            // back_duration from CADL COLLISION_RECOVERY protocol, with ±0.5s jitter
            float baseDuration = SimulatorConfigurator.GetBackDuration(2f);
            maxT = Random.Range(baseDuration, baseDuration + 1.5f);

            RMColor = color;
            isMCPMode = mcpMode;
        }

        public void Handle()
        {
            if (action == CarAction.Wait)
            {
                // Do nothing - waiting for LLM set_path
                mouse.Stop();
            }
            else if (action == CarAction.Back)
            {
                if (backHandler.Update())
                {
                    if (isReversingForPath)
                    {
                        // set_path reverse completed - continue with Go
                        action = CarAction.Go;
                        isReversingForPath = false;
                        Debug.Log("[Handler] Reverse for path completed, resuming Go. RM: " + RMColor);
                    }
                    else if (isMCPMode)
                    {
                        // MCP mode: Collision reverse completed - enter Wait state for LLM
                        action = CarAction.Wait;
                        CollisionBackCompleted = true;
                        Debug.Log("[Handler] Back completed, waiting for LLM instruction. RM: " + RMColor);
                    }
                    else
                    {
                        // Original D-SoS/C-SoS mode: just continue with Go
                        action = CarAction.Go;
                        // Notify Pilot_CSoS that collision backup is complete so it can
                        // reset preCross and re-request permission at the next intersection.
                        CollisionBackCompleted = true;
                    }
                }
            }
            else if (Mathf.Min(mouse.distSensor.Distance(0), mouse.distSensor.Distance(3)) < 0.1f)
            {
                t += Time.deltaTime;
                if (t > maxT)
                {
                    t = 0;
                    action = CarAction.Back;
                    backHandler.Reset();
                    if(!isCollision){
                        Debug.Log( "[Collision]" + " RM: " + RMColor + " time[s]: " +Time.time);
                        isCollision = true;
                        CADLConfig.CADLMetricsCollector.RecordCollision();
                    }
                }

                mouse.Stop();
            }
            else if (action == CarAction.Cross)
            {
                if (crossHandler.Update(mouse.transform)) action = CarAction.Go;
                isCollision = false;
            }
            else
            {
                t = 0;
                goHandler.Update();
                isCollision = false;
            }
        }

        /// <summary>
        /// Resume from Wait state (called after LLM set_path).
        /// </summary>
        public void ResumeFromWait()
        {
            if (action == CarAction.Wait)
            {
                action = CarAction.Go;
                CollisionBackCompleted = false;
                isCollision = false;
                Debug.Log("[Handler] Resumed from Wait. RM: " + RMColor);
            }
        }

        /// <summary>
        /// Enter Wait state (for LLM yield command).
        /// </summary>
        public void SetWait()
        {
            action = CarAction.Wait;
            Debug.Log("[Handler] SetWait called. RM: " + RMColor);
        }

        public void SetCross(Vector3 center, Vector3 dest)
        {
            action = CarAction.Cross;
            crossHandler.Reset(center, dest);
        }

        public void SetBack()
        {
            action = CarAction.Back;
            backHandler.Reset();
        }

        /// <summary>
        /// Reverse 180 degrees and then continue with Go (for set_path reverse direction).
        /// </summary>
        public void ReverseAndGo()
        {
            action = CarAction.Back;
            backHandler.Reset();
            isReversingForPath = true;
            Debug.Log("[Handler] ReverseAndGo initiated. RM: " + RMColor);
        }
    }
}
