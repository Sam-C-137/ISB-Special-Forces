using UnityEngine;

namespace ISBSSOFExtra
{
    internal sealed class EmergencyLightCulling : CullingObject
    {
        private Light _light;
        private Transform _cachedTransform;
        private CullingManager _manager;
        private bool _registered, _budgetAllowed, _cameraVisible;
        private float _pulse, _distance, _nextUpdate, _nextRegistration;
        internal bool BudgetAllowed => _budgetAllowed;
        internal float Distance => _distance;

        public override void Awake()
        {
            _cachedTransform = transform;
            Index = -1;
        }

        internal void Configure(Light light)
        {
            _cachedTransform = transform;
            _light = light;
            if (!_registered)
                Index = -1;
            Radius = light.range;
            CullDistance = 70f;
            CullByDistanceOnly = false;
            IsAutocullVisible = true;
        }

        public new void Start() => TryRegister();
        private void TryRegister()
        {
            var manager = CullingManager.Instance;
            if (_registered && ReferenceEquals(manager, _manager))
                return;
            if (_cachedTransform == null || !isActiveAndEnabled)
                return;
            if (_registered)
            {
                _registered = false;
                Index = -1;
            }

            if (Time.realtimeSinceStartup < _nextRegistration)
                return;
            _nextRegistration = Time.realtimeSinceStartup + .5f;
            _manager = manager;
            if (_manager == null)
                return;
            Register();
            _registered = Index >= 0;
            if (_registered)
            {
                _cachedTransform.hasChanged = true;
                base.CustomUpdate();
            }
        }

        internal bool Maintain(Vector3 origin, Plane[] planes)
        {
            TryRegister();
            if (_light == null || _cachedTransform == null || !isActiveAndEnabled)
                return false;
            Vector3 position = _cachedTransform.position;
            _distance = (position - origin).sqrMagnitude;
            bool visible = _distance < 4900f && GeometryUtility.TestPlanesAABB(planes, new Bounds(position, Vector3.one * (_light.range * 2f)));
            _cameraVisible = visible;
            if (!_registered)
                _isVisible = visible;
            return visible;
        }

        internal void SetBudget(bool allowed)
        {
            if (_budgetAllowed == allowed)
                return;
            _budgetAllowed = allowed;
            Apply();
        }

        public override Transform GetTransform() => _cachedTransform;
        public override void SetVisibility(bool visible)
        {
            if (_isVisible == visible)
                return;
            _isVisible = visible;
            Apply();
        }

        internal void SetPulse(float intensity)
        {
            _pulse = intensity;
            if (_budgetAllowed)
                Apply();
        }

        public override void CustomUpdate()
        {
            if (!_budgetAllowed || !_cameraVisible || !isActiveAndEnabled || Time.realtimeSinceStartup < _nextUpdate)
                return;
            _nextUpdate = Time.realtimeSinceStartup + .1f;
            Apply();
        }

        private void Apply()
        {
            if (_light == null)
                return;
            float fade = Mathf.Clamp01((4900f - _distance) / 2875f);
            bool lit = _budgetAllowed && _cameraVisible && IsAutocullVisible && isActiveAndEnabled && _pulse > 0f && fade > 0f;
            if (_light.enabled != lit)
                _light.enabled = lit;
            if (lit)
            {
                float intensity = _pulse * fade;
                if (_light.intensity != intensity)
                    _light.intensity = intensity;
            }
        }

        private void OnEnable() => Apply();
        private void OnDisable()
        {
            if (_light != null)
                _light.enabled = false;
        }

        public override void OnDestroy()
        {
            if (_registered && _manager != null && ReferenceEquals(CullingManager.Instance, _manager))
                _manager.Unregister(this);
            _registered = false;
        }
    }
}
