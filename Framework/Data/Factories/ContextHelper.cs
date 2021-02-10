namespace Framework.Data.Factories
{
    public class ContextHelper
    {
        public bool SaveChangesOnDispose { get; set; }
        public bool IsDisposed { get; set; }
        private int count = 0;

        public ContextHelper(bool saveChangedOnDispose)
        {
            this.SaveChangesOnDispose = saveChangedOnDispose;
        }

        public void Register()
        {
            count++;
        }

        public bool DoDispose()
        {
            count--;
            return count <= 0;
        }
    }
}
