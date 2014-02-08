﻿/*
 * @author Valentin Simonov / http://va.lent.in/
 */

using System;
using System.Collections.Generic;
using TouchScript.Events;
using TouchScript.Clusters;
using TouchScript.Hit;
using TouchScript.Layers;
using TouchScript.Utils;
using UnityEngine;

namespace TouchScript.Gestures
{
    /// <summary>
    /// Base class for all gestures
    /// </summary>
    public abstract class Gesture : MonoBehaviour
    {
        #region Constants

        public const string STATE_CHANGED_MESSAGE = "OnGestureStateChanged";

        /// <summary>
        /// Possible states of a gesture.
        /// </summary>
        public enum GestureState
        {
            /// <summary>
            /// Gesture is possible
            /// </summary>
            Possible,

            /// <summary>
            /// Continuous gesture has just begun
            /// </summary>
            Began,

            /// <summary>
            /// Started continuous gesture is updated
            /// </summary>
            Changed,

            /// <summary>
            /// Continuous gesture is ended
            /// </summary>
            Ended,

            /// <summary>
            /// Gesture is cancelled
            /// </summary>
            Cancelled,

            /// <summary>
            /// Gesture is failed by itself or by another recognized gesture
            /// </summary>
            Failed,

            /// <summary>
            /// Gesture is recognized
            /// </summary>
            Recognized = Ended
        }

        #endregion

        #region Events

        /// <summary>
        /// Occurs when gesture changes state.
        /// </summary>
        public event EventHandler<GestureStateChangeEventArgs> StateChanged
        {
            add { stateChangedInvoker += value; }
            remove { stateChangedInvoker -= value; }
        }

        // Needed to overcome iOS AOT limitations
        private EventHandler<GestureStateChangeEventArgs> stateChangedInvoker;

        #endregion

        #region Public properties

        public bool CombineTouchPoints
        {
            get { return combineTouchPoints; }
            set { combineTouchPoints = value; }
        }

        /// <summary>
        /// Time interval before gesture is recognized to combine all lifted touch points into a cluster and calculate their screen positions.
        /// </summary>
        public float CombineTouchPointsInterval
        {
            get { return combineTouchPointsInterval; }
            set { combineTouchPointsInterval = value; }
        }

        public bool UseSendMessage
        {
            get { return useSendMessage; }
            set { useSendMessage = value; }
        }

        public bool SendStateChangeMessages
        {
            get { return sendStateChangeMessages; }
            set { sendStateChangeMessages = value; }
        }

        public GameObject SendMessageTarget
        {
            get { return sendMessageTarget; }
            set
            {
                sendMessageTarget = value;
                if (value == null) sendMessageTarget = gameObject;
            }
        }

        /// <summary>
        /// Current gesture state.
        /// </summary>
        public GestureState State
        {
            get { return state; }
            private set
            {
                PreviousState = state;
                state = value;

                switch (value)
                {
                    case GestureState.Possible:
                        onPossible();
                        break;
                    case GestureState.Began:
                        onBegan();
                        break;
                    case GestureState.Changed:
                        onChanged();
                        break;
                    case GestureState.Recognized:
                        onRecognized();
                        break;
                    case GestureState.Failed:
                        onFailed();
                        break;
                    case GestureState.Cancelled:
                        onCancelled();
                        break;
                }

                if (stateChangedInvoker != null) stateChangedInvoker(this, new GestureStateChangeEventArgs(state, PreviousState));
                if (useSendMessage && sendStateChangeMessages) sendMessageTarget.SendMessage(STATE_CHANGED_MESSAGE, this, SendMessageOptions.DontRequireReceiver);
            }
        }

        /// <summary>
        /// Previous gesture state.
        /// </summary>
        public GestureState PreviousState { get; private set; }

        /// <summary>
        /// Transformation center in screen coordinates.
        /// </summary>
        public virtual Vector2 ScreenPosition
        {
            get
            {
                if (activeTouches.Count == 0)
                {
                    if (!TouchPoint.IsInvalidPosition(cachedScreenPosition)) return cachedScreenPosition;
                    return TouchPoint.INVALID_POSITION;
                }
                return Cluster.Get2DCenterPosition(activeTouches);
            }
        }

        /// <summary>
        /// Previous transformation center in screen coordinates.
        /// </summary>
        public virtual Vector2 PreviousScreenPosition
        {
            get
            {
                if (activeTouches.Count == 0)
                {
                    if (!TouchPoint.IsInvalidPosition(cachedPreviousScreenPosition)) return cachedPreviousScreenPosition;
                    return TouchPoint.INVALID_POSITION;
                }
                return Cluster.GetPrevious2DCenterPosition(activeTouches);
            }
        }

        /// <summary>
        /// Transformation center in normalized screen coordinates.
        /// </summary>
        public Vector2 NormalizedScreenPosition
        {
            get
            {
                var position = ScreenPosition;
                if (TouchPoint.IsInvalidPosition(position)) return TouchPoint.INVALID_POSITION;
                return new Vector2(position.x/Screen.width, position.y/Screen.height);
            }
        }

        /// <summary>
        /// Previous center in screen coordinates.
        /// </summary>
        public Vector2 PreviousNormalizedScreenPosition
        {
            get
            {
                var position = PreviousScreenPosition;
                if (TouchPoint.IsInvalidPosition(position)) return TouchPoint.INVALID_POSITION;
                return new Vector2(position.x/Screen.width, position.y/Screen.height);
            }
        }

        /// <summary>
        /// List of gesture's active touch points.
        /// </summary>
        public IList<TouchPoint> ActiveTouches
        {
            get { return activeTouches.AsReadOnly(); }
        }

        /// <summary>
        /// An object implementing <see cref="IGestureDelegate"/> to be asked for gesture specific actions.
        /// </summary>
        public IGestureDelegate Delegate { get; set; }

        #endregion

        #region Private variables

        /// <summary>
        /// Reference to global GestureManager.
        /// </summary>
        protected IGestureManager gestureManager
        {
            get { return gestureManagerInstance; }
        }

        /// <summary>
        /// Reference to global TouchManager.
        /// </summary>
        protected ITouchManager touchManager { get; private set; }

        /// <summary>
        /// Touch points the gesture currently owns and works with.
        /// </summary>
        protected List<TouchPoint> activeTouches = new List<TouchPoint>();

        [SerializeField]
        private bool combineTouchPoints = false;

        [SerializeField]
        private float combineTouchPointsInterval = .3f;

        [SerializeField]
        private bool useSendMessage = false;

        [SerializeField]
        private bool sendStateChangeMessages = false;

        [SerializeField]
        private GameObject sendMessageTarget;

        [SerializeField]
        private List<Gesture> friendlyGestures = new List<Gesture>();

        private List<int> friendlyGestureIds = new List<int>();

        private TimedTouchSequence touchSequence = new TimedTouchSequence();
        private GestureManagerInstance gestureManagerInstance;
        private GestureState state = GestureState.Possible;

        /// <summary>
        /// Cached screen position. 
        /// Used to keep tap's position which can't be calculated from touch points when the gesture is recognized since all touch points are gone.
        /// </summary>
        private Vector2 cachedScreenPosition;

        /// <summary>
        /// Cached previous screen position.
        /// Used to keep tap's position which can't be calculated from touch points when the gesture is recognized since all touch points are gone.
        /// </summary>
        private Vector2 cachedPreviousScreenPosition;

        #endregion

        #region Unity methods

        /// <summary>
        /// Unity3d Start handler.
        /// </summary>
        protected virtual void OnEnable()
        {
            touchManager = TouchManager.Instance;
            gestureManagerInstance = GestureManager.Instance as GestureManagerInstance;

            if (touchManager == null) Debug.LogError("No TouchManager found! Please add an instance of TouchManager to the scene!");
            if (gestureManagerInstance == null) Debug.LogError("No GesturehManager found! Please add an instance of GesturehManager to the scene!");

            foreach (var gesture in friendlyGestures)
            {
                AddFriendlyGesture(gesture);
            }

            if (sendMessageTarget == null) sendMessageTarget = gameObject;
            Reset();
        }

        /// <summary>
        /// Unity3d OnDisable handler.
        /// </summary>
        protected virtual void OnDisable()
        {
            setState(GestureState.Failed);

            gestureManagerInstance = null;
            touchManager = null;
        }

        #endregion

        #region Public methods

        /// <summary>
        /// Adds a friendly gesture.
        /// </summary>
        /// <param name="gesture">The gesture.</param>
        public virtual void AddFriendlyGesture(Gesture gesture)
        {
            if (gesture == null || gesture == this) return;

            RegisterFriendlyGesture(gesture);
            gesture.RegisterFriendlyGesture(this);
        }

        /// <summary>
        /// Removes a friendly gesture.
        /// </summary>
        /// <param name="gesture">The gesture.</param>
        public virtual void RemoveFriendlyGesture(Gesture gesture)
        {
            if (gesture == null || gesture == this) return;

            UnregisterFriendlyGesture(gesture);
            gesture.UnregisterFriendlyGesture(this);
        }

        /// <summary>
        /// Determines whether the specified gesture is friendly.
        /// </summary>
        /// <param name="gesture">The gesture.</param>
        /// <returns>
        ///   <c>true</c> if the specified gesture is friendly; otherwise, <c>false</c>.
        /// </returns>
        public bool IsFriendly(Gesture gesture)
        {
            return friendlyGestureIds.Contains(gesture.GetInstanceID());
        }

        /// <summary>
        /// Gets result of casting a ray from gesture touch points' centroid screen position.
        /// </summary>
        /// <returns>true if ray hits gesture's target; otherwise, false.</returns>
        public virtual bool GetTargetHitResult()
        {
            TouchHit hit;
            return GetTargetHitResult(ScreenPosition, out hit);
        }

        /// <summary>
        /// Gets result of casting a ray from gesture touch points centroid screen position.
        /// </summary>
        /// <param name="hit">Raycast result</param>
        /// <returns>true if ray hits gesture's target; otherwise, false.</returns>
        public virtual bool GetTargetHitResult(out TouchHit hit)
        {
            return GetTargetHitResult(ScreenPosition, out hit);
        }

        /// <summary>
        /// Gets result of casting a ray from specific screen position.
        /// </summary>
        /// <param name="position">The position.</param>
        /// <returns>true if ray hits gesture's target; otherwise, false.</returns>
        public virtual bool GetTargetHitResult(Vector2 position)
        {
            TouchHit hit;
            return GetTargetHitResult(position, out hit);
        }

        /// <summary>
        /// Gets result of casting a ray from specific screen position.
        /// </summary>
        /// <param name="position">The position.</param>
        /// <param name="hit">Raycast result.</param>
        /// <returns>true if ray hits gesture's target; otherwise, false.</returns>
        public virtual bool GetTargetHitResult(Vector2 position, out TouchHit hit)
        {
            TouchLayer layer = null;
            if (!touchManager.GetHitTarget(position, out hit, out layer)) return false;

            if (transform == hit.Transform || hit.Transform.IsChildOf(transform)) return true;
            return false;
        }

        /// <summary>
        /// Determines whether gesture controls a touch point.
        /// </summary>
        /// <param name="touch">The touch.</param>
        /// <returns>
        ///   <c>true</c> if gesture controls the touch point; otherwise, <c>false</c>.
        /// </returns>
        public bool HasTouchPoint(TouchPoint touch)
        {
            return activeTouches.Contains(touch);
        }

        /// <summary>
        /// Determines whether this instance can prevent the specified gesture.
        /// </summary>
        /// <param name="gesture">The gesture.</param>
        /// <returns>
        ///   <c>true</c> if this instance can prevent the specified gesture; otherwise, <c>false</c>.
        /// </returns>
        public virtual bool CanPreventGesture(Gesture gesture)
        {
            if (Delegate == null)
            {
                if (gesture.CanBePreventedByGesture(this)) return !IsFriendly(gesture);
                return false;
            }
            return !Delegate.ShouldRecognizeSimultaneously(this, gesture);
        }

        /// <summary>
        /// Determines whether this instance can be prevented by specified gesture.
        /// </summary>
        /// <param name="gesture">The gesture.</param>
        /// <returns>
        ///   <c>true</c> if this instance can be prevented by specified gesture; otherwise, <c>false</c>.
        /// </returns>
        public virtual bool CanBePreventedByGesture(Gesture gesture)
        {
            if (Delegate == null) return !IsFriendly(gesture);
            return !Delegate.ShouldRecognizeSimultaneously(this, gesture);
        }

        /// <summary>
        /// Specifies if gesture can receive this specific touch point.
        /// </summary>
        /// <param name="touch">The touch.</param>
        /// <returns><c>true</c> if this touch should be received by the gesture; otherwise, <c>false</c>.</returns>
        public virtual bool ShouldReceiveTouch(TouchPoint touch)
        {
            if (Delegate == null) return true;
            return Delegate.ShouldReceiveTouch(this, touch);
        }

        /// <summary>
        /// Specifies if gesture can begin or recognize.
        /// </summary>
        /// <returns><c>true</c> if gesture should begin; otherwise, <c>false</c>.</returns>
        public virtual bool ShouldBegin()
        {
            if (Delegate == null) return true;
            return Delegate.ShouldBegin(this);
        }

        #endregion

        #region Internal functions

        internal void SetState(GestureState value)
        {
            setState(value);
        }

        internal void Reset()
        {
            activeTouches.Clear();
            reset();
        }

        internal void TouchesBegan(IList<TouchPoint> touches)
        {
            activeTouches.AddRange(touches);
            touchesBegan(touches);
        }

        internal void TouchesMoved(IList<TouchPoint> touches)
        {
            touchesMoved(touches);
        }

        internal void TouchesEnded(IList<TouchPoint> touches)
        {
            activeTouches.RemoveAll(touches.Contains);
            touchesEnded(touches);
        }

        internal void TouchesCancelled(IList<TouchPoint> touches)
        {
            activeTouches.RemoveAll(touches.Contains);
            touchesCancelled(touches);
        }

        internal void RegisterFriendlyGesture(Gesture gesture)
        {
            if (gesture == this || friendlyGestureIds.Contains(gesture.GetInstanceID())) return;

            friendlyGestureIds.Add(gesture.GetInstanceID());
        }

        internal void UnregisterFriendlyGesture(Gesture gesture)
        {
            friendlyGestureIds.Remove(gesture.GetInstanceID());
        }

        #endregion

        #region Protected methods

        protected virtual bool shouldCacheTouchPointPosition(TouchPoint value)
        {
            return true;
        }

        /// <summary>
        /// Tries to change gesture state.
        /// </summary>
        /// <param name="value">New state.</param>
        /// <returns><c>true</c> if state was changed; otherwise, <c>false</c>.</returns>
        protected bool setState(GestureState value)
        {
            if (gestureManagerInstance == null) return false;
            if (value == state && state != GestureState.Changed) return false;

            var newState = gestureManagerInstance.GestureChangeState(this, value);
            State = newState;

            return value == newState;
        }

        /// <summary>
        /// Manually ignore touch.
        /// </summary>
        /// <param name="touch">Touch to ignore.</param>
        protected void ignoreTouch(TouchPoint touch)
        {
            activeTouches.Remove(touch);
        }

        #endregion

        #region Callbacks

        /// <summary>
        /// Called when new touches appear.
        /// </summary>
        /// <param name="touches">The touches.</param>
        protected virtual void touchesBegan(IList<TouchPoint> touches)
        {}

        /// <summary>
        /// Called for moved touches.
        /// </summary>
        /// <param name="touches">The touches.</param>
        protected virtual void touchesMoved(IList<TouchPoint> touches)
        {}

        /// <summary>
        /// Called if touches are removed.
        /// </summary>
        /// <param name="touches">The touches.</param>
        protected virtual void touchesEnded(IList<TouchPoint> touches)
        {
            if (combineTouchPoints)
            {
                foreach (var touch in touches)
                {
                    touchSequence.Add(touch, Time.time);
                }

                if (activeTouches.Count == 0)
                {
                    // Checking which points were removed in clusterExistenceTime seconds to set their centroid as cached screen position
                    var cluster = touchSequence.FindTouchPointsLaterThan(Time.time - combineTouchPointsInterval, shouldCacheTouchPointPosition);
                    cachedScreenPosition = Cluster.Get2DCenterPosition(cluster);
                    cachedPreviousScreenPosition = Cluster.GetPrevious2DCenterPosition(cluster);
                }
            } else
            {
                if (activeTouches.Count == 0)
                {
                    var lastPoint = touches[touches.Count - 1];
                    if (shouldCacheTouchPointPosition(lastPoint))
                    {
                        cachedScreenPosition = lastPoint.Position;
                        cachedPreviousScreenPosition = lastPoint.PreviousPosition;
                    } else
                    {
                        cachedScreenPosition = TouchPoint.INVALID_POSITION;
                        cachedPreviousScreenPosition = TouchPoint.INVALID_POSITION;
                    }
                }
            }
        }

        /// <summary>
        /// Called when touches are cancelled.
        /// </summary>
        /// <param name="touches">The touches.</param>
        protected virtual void touchesCancelled(IList<TouchPoint> touches)
        {}

        /// <summary>
        /// Called to reset gesture state after it fails or recognizes.
        /// </summary>
        protected virtual void reset()
        {}

        /// <summary>
        /// Called when state is changed to Possible.
        /// </summary>
        protected virtual void onPossible()
        {}

        /// <summary>
        /// Called when state is changed to Began.
        /// </summary>
        protected virtual void onBegan()
        {}

        /// <summary>
        /// Called when state is changed to Changed.
        /// </summary>
        protected virtual void onChanged()
        {}

        /// <summary>
        /// Called when state is changed to Recognized.
        /// </summary>
        protected virtual void onRecognized()
        {}

        /// <summary>
        /// Called when state is changed to Failed.
        /// </summary>
        protected virtual void onFailed()
        {}

        /// <summary>
        /// Called when state is changed to Cancelled.
        /// </summary>
        protected virtual void onCancelled()
        {}

        #endregion
    }
}