using RasPiMouse;

namespace LineTrace.Handlers
{
    public class BackHandler
    {
        private readonly Mouse mouse;

        private bool b;
        private int i;

        public BackHandler(Mouse mouse)
        {
            this.mouse = mouse;
        }

        public void Reset()
        {
            b = true;
            i = 0;
        }

        public bool Update()
        {
            if (b)
            {
                if (mouse.lineSensor.MinLightness(new[] {0, 1, 2, 3}) > 0.6f)
                {
                    i++;
                    if (i >= 3) b = false;
                }
            }
            else
            {
                if (mouse.lineSensor.MaxLightness(new[] {1, 2}) < 0.5f)
                {
                    return true;
                }
            }

            mouse.Turn(1);

            return false;
        }
    }
}