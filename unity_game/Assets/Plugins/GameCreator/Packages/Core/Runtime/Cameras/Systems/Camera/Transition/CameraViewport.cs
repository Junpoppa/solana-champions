using System;
using GameCreator.Runtime.Common;
using UnityEngine;

namespace GameCreator.Runtime.Cameras
{
    [Serializable]
    public class CameraViewport
    {
        private const float EPSILON = 0.001f;
        
        // MEMBERS: -------------------------------------------------------------------------------
        
        [NonSerialized] private TCamera m_Camera;
        
        // PROPERTIES: ----------------------------------------------------------------------------

        public bool Projection
        {
            get => this.m_Camera.Get<Camera>().orthographic;
            private set => this.m_Camera.Get<Camera>().orthographic = value;
        }

        public float FieldOfView
        {
            get => this.m_Camera.Get<Camera>().fieldOfView;
            private set => this.m_Camera.Get<Camera>().fieldOfView = value;
        }
        
        public float OrthographicSize
        {
            get => this.m_Camera.Get<Camera>().orthographicSize;
            private set => this.m_Camera.Get<Camera>().orthographicSize = value;
        }

        // INITIALIZERS: --------------------------------------------------------------------------

        internal void OnEnable(TCamera camera)
        {
            this.m_Camera = camera;

            camera.Transition.EventCut -= this.OnChangeShot;
            camera.Transition.EventTransition -= this.OnChangeShot;
            
            camera.Transition.EventCut += this.OnChangeShot;
            camera.Transition.EventTransition += this.OnChangeShot;
        }

        internal void OnDisable(TCamera camera)
        {
            camera.Transition.EventCut -= this.OnChangeShot;
            camera.Transition.EventTransition -= this.OnChangeShot;
        }

        // PUBLIC METHODS: ------------------------------------------------------------------------
        
        public void SetProjection(bool isOrthographic)
        {
            this.Projection = isOrthographic;
        }
        
        public void SetFieldOfView(float value, float duration, Easing.Type easing)
        {
            if (duration <= EPSILON)
            {
                this.FieldOfView = value;
                return;
            }

            ITweenInput tween = new TweenInput<float>(
                this.FieldOfView,
                value,
                duration,
                (a, b, t) => this.FieldOfView = Mathf.Lerp(a, b, t),
                Tween.GetHash(typeof(TCamera), "projection"),
                easing,
                this.m_Camera.Time.UpdateTime
            );
            
            Tween.To(this.m_Camera.gameObject, tween);
        }
        
        public void SetOrthographicSize(float value, float duration, Easing.Type easing)
        {
            if (duration <= EPSILON)
            {
                this.OrthographicSize = value;
                return;
            }

            ITweenInput tween = new TweenInput<float>(
                this.OrthographicSize,
                value,
                duration,
                (a, b, t) => this.OrthographicSize = Mathf.Lerp(a, b, t),
                Tween.GetHash(typeof(TCamera), "orthographicSize"),
                easing,
                this.m_Camera.Time.UpdateTime
            );
            
            Tween.To(this.m_Camera.gameObject, tween);
        }

        // PRIVATE METHODS: -----------------------------------------------------------------------
        
        private void OnChangeShot(ShotCamera shotCamera)
        {
            this.OnChangeShot(shotCamera, 0f, Easing.Type.Linear);
        }
        
        private void OnChangeShot(ShotCamera shotCamera, float duration, Easing.Type easing)
        {
            IShotType shotType = shotCamera.ShotType;
            if (shotType.GetSystem(ShotSystemViewport.ID) is not ShotSystemViewport view)
            {
                return;
            }

            if (view.ChangeProjection) this.SetProjection(view.Projection);
            if (view.ChangeFieldOfView) this.SetFieldOfView(view.FieldOfView, duration, easing);
            if (view.ChangeOrthographicSize) this.SetFieldOfView(view.OrthographicSize, duration, easing);

            // this.OnUpdateCamera();
        }
    }
}