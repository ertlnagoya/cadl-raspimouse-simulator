using RasPiMouse;

namespace LineTrace.Handlers
{
    public class StopHandler
    {
        private readonly Mouse mouse;

        public StopHandler(Mouse mouse)
        {
            this.mouse = mouse;
        }

        public void FixedUpdate()
        {
            mouse.Stop();
        }
    }
}