namespace ISBSSOFExtra
{
    internal sealed class LabsMenuRequestRetry
    {
        private int _attempts;
        private float _due;
        internal void Reset()
        {
            _attempts = 0;
            _due = 0;
        }

        internal void Succeeded(float now)
        {
            _attempts = 0;
            _due = now + 30f;
        }

        internal void Failed(float now)
        {
            _due = now + System.Math.Min(30, 5 << _attempts);
            _attempts = System.Math.Min(3, _attempts + 1);
        }

        internal bool Take(float now, bool visible)
        {
            if (!visible || _due <= 0 || now < _due)
                return false;
            _due = 0;
            return true;
        }
    }
}
