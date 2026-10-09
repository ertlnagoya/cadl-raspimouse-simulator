using RasPiMouse;

namespace LineTrace.Handlers
{
    public class GoHandler
    {
        private readonly Mouse mouse;

        public GoHandler(Mouse mouse)
        {
            this.mouse = mouse;
        }

        public void Update()
        {
            if (mouse.lineSensor.Lightness(1) > 0.6f)
            {
                mouse.Turn(1);
            }
            else if (mouse.lineSensor.Lightness(2) > 0.6f)
            {
                mouse.Turn(-1);
            }
            else
            {
                mouse.Go(1);
            }
        }
    }
}