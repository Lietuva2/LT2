using System;

namespace Framework.Bus
{
    public static class Bus
    {
        private static Func<IBus> factory;

        public static Func<IBus> Factory
        {
            get
            {
                if (factory == null)
                {
                    throw new Exception("Bus factory not set");
                }

                return factory;
            }

            set
            {
                factory = value;
            }
        }

        public static IBus Get()
        {
            return Factory();
        }
    }
}
