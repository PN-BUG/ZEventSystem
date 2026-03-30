// EventCenter.cs (Full Version)
// - O(1) trigger via combined delegate (no DynamicInvoke)
// - Supports: string eventName, enum eventName, IEventInfo type eventName
// - Supports: Add/Remove (precise), RemoveOne (remove all handlers by listener for one event), RemoveALL (remove all registrations)
// - Supports: TR (query) one-to-one by signature/eventName
// - Clear() truly clears all generic tables actually used (no need to list signatures)
// - Enum->string key cached (fast, supports any enum underlying type)
// NOTE: Enum key format: "{EnumType.FullName}.{EnumValueName}"

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace ZEventSystem
{
    public interface IOnlyOneID { int Rid { get; set; } }

    public interface IListener
    {
        string ListenerName
        {
            get
            {
                if (this is IOnlyOneID one)
                    return GetType().Name + "_" + one.GetID();

                if (this is UnityEngine.Object uo)
                    return GetType().Name + "_" + uo.GetInstanceID();

                return GetType().Name + "_" + GetHashCode();
            }
        }
    }

    #region OnlyOneID Pool (Unity-friendly)
    internal static class OnlyOneIdPool
    {
        private static readonly object _lockObj = new object();
        private static readonly SortedSet<int> _used = new SortedSet<int>();
        private static readonly Queue<int> _unused = new Queue<int>();

        public static void Clear()
        {
            lock (_lockObj)
            {
                _used.Clear();
                _unused.Clear();
            }
        }

        public static int Init(IOnlyOneID self)
        {
            lock (_lockObj)
            {
                Release(self);

                int id = _unused.Count > 0 ? _unused.Dequeue() : (_used.Count > 0 ? _used.Max + 1 : 1);
                self.Rid = id;
                _used.Add(id);
                return id;
            }
        }

        public static int Get(IOnlyOneID self) => self.Rid != 0 ? self.Rid : Init(self);

        public static void Release(IOnlyOneID self)
        {
            if (self == null || self.Rid == 0) return;
            lock (_lockObj)
            {
                int id = self.Rid;
                if (_used.Remove(id)) _unused.Enqueue(id);
                self.Rid = 0;
            }
        }
    }

    public static class OnlyOneIdExt
    {
        public static int Init(this IOnlyOneID self) => OnlyOneIdPool.Init(self);
        public static void TryInit(this IOnlyOneID self) { if (self.Rid == 0) OnlyOneIdPool.Init(self); }
        public static int GetID(this IOnlyOneID self) => OnlyOneIdPool.Get(self);
        public static void ReleaseID(this IOnlyOneID self) => OnlyOneIdPool.Release(self);
    }
    #endregion

    public static class EventCenter
    {
        #region Reverse Index (listener -> regs) + Removers

        private readonly struct RegKey : IEquatable<RegKey>
        {
            public readonly string EventName;
            public readonly Type DelegateType;
            public readonly bool IsTR;

            public RegKey(string eventName, Type delegateType, bool isTR)
            {
                EventName = eventName;
                DelegateType = delegateType;
                IsTR = isTR;
            }

            public bool Equals(RegKey other) =>
                EventName == other.EventName && DelegateType == other.DelegateType && IsTR == other.IsTR;

            public override bool Equals(object obj) => obj is RegKey other && Equals(other);
            public override int GetHashCode() => HashCode.Combine(EventName, DelegateType, IsTR);
        }

        private static readonly Dictionary<string, HashSet<RegKey>> _listenerRegs = new();

        private interface IRemover
        {
            void Remove(string eventName, string listenerName);
            void RemoveTR(string eventName, string listenerName);
        }

        private sealed class Remover<TDelegate> : IRemover where TDelegate : Delegate
        {
            public void Remove(string eventName, string listenerName) => EventTable<TDelegate>.RemoveListener(eventName, listenerName);
            public void RemoveTR(string eventName, string listenerName) => EventTableTR<TDelegate>.RemoveListener(eventName, listenerName);
        }

        private static readonly Dictionary<Type, IRemover> _removers = new();

        private static IRemover GetRemover(Type delegateType)
        {
            if (_removers.TryGetValue(delegateType, out var r)) return r;

            var removerType = typeof(Remover<>).MakeGenericType(delegateType);
            r = (IRemover)Activator.CreateInstance(removerType);
            _removers.Add(delegateType, r);
            return r;
        }

        private static void Track(string listenerName, string eventName, Type delegateType, bool isTR)
        {
            if (!_listenerRegs.TryGetValue(listenerName, out var set))
            {
                set = new HashSet<RegKey>();
                _listenerRegs.Add(listenerName, set);
            }
            set.Add(new RegKey(eventName, delegateType, isTR));
        }

        #endregion

        #region Clear-All Registry (so Clear() actually clears all used signatures)

        // Each generic EventTable<TDelegate> / EventTableTR<TDelegate> registers its _events.Clear once.
        private static readonly List<Action> _clearAll = new();
        private static readonly HashSet<(bool isTR, Type t)> _registeredClear = new();

        private static void RegisterClear(bool isTR, Type t, Action clearer)
        {
            if (_registeredClear.Add((isTR, t)))
                _clearAll.Add(clearer);
        }

        #endregion

        #region Buckets

        private sealed class Bucket<TDelegate> where TDelegate : Delegate
        {
            private readonly Dictionary<string, TDelegate> _perListener = new(16);
            private TDelegate _combined;

            public int Count => _perListener.Count;
            public TDelegate Combined => _combined;

            public void Add(string listenerName, TDelegate action)
            {
                if (_perListener.TryGetValue(listenerName, out var old))
                {
                    var merged = (TDelegate)Delegate.Combine(old, action);
                    _perListener[listenerName] = merged;

                    _combined = (TDelegate)Delegate.Remove(_combined, old);
                    _combined = (TDelegate)Delegate.Combine(_combined, merged);
                }
                else
                {
                    _perListener.Add(listenerName, action);
                    _combined = (TDelegate)Delegate.Combine(_combined, action);
                }
            }

            public void Remove(string listenerName)
            {
                if (_perListener.TryGetValue(listenerName, out var old))
                {
                    _perListener.Remove(listenerName);
                    _combined = (TDelegate)Delegate.Remove(_combined, old);
                }
            }

            public void Remove(string listenerName, TDelegate action)
            {
                if (_perListener.TryGetValue(listenerName, out var old))
                {
                    var newDel = (TDelegate)Delegate.Remove(old, action);

                    _combined = (TDelegate)Delegate.Remove(_combined, old);

                    if (newDel == null)
                        _perListener.Remove(listenerName);
                    else
                    {
                        _perListener[listenerName] = newDel;
                        _combined = (TDelegate)Delegate.Combine(_combined, newDel);
                    }
                }
            }
        }

        private sealed class BucketTR<TDelegate> where TDelegate : Delegate
        {
            private string _ownerListenerName;
            private TDelegate _func;

            public bool Has => _func != null;

            public void Set(string listenerName, TDelegate func)
            {
                _ownerListenerName = listenerName;
                _func = func;
            }

            public void Remove(string listenerName)
            {
                if (_ownerListenerName == listenerName)
                {
                    _ownerListenerName = null;
                    _func = null;
                }
            }

            public TDelegate Func => _func;
        }

        #endregion

        #region Tables (per delegate signature)

        private static class EventTable<TDelegate> where TDelegate : Delegate
        {
            private static readonly Dictionary<string, Bucket<TDelegate>> _events = new();

            static EventTable()
            {
                RegisterClear(isTR: false, typeof(TDelegate), _events.Clear);
            }

            public static Bucket<TDelegate> GetOrCreate(string eventName)
            {
                if (!_events.TryGetValue(eventName, out var bucket))
                {
                    bucket = new Bucket<TDelegate>();
                    _events.Add(eventName, bucket);
                }
                return bucket;
            }

            public static bool TryGet(string eventName, out Bucket<TDelegate> bucket)
                => _events.TryGetValue(eventName, out bucket);

            public static void RemoveListener(string eventName, string listenerName)
            {
                if (_events.TryGetValue(eventName, out var bucket))
                {
                    bucket.Remove(listenerName);
                    if (bucket.Count == 0) _events.Remove(eventName);
                }
            }
        }

        private static class EventTableTR<TDelegate> where TDelegate : Delegate
        {
            private static readonly Dictionary<string, BucketTR<TDelegate>> _events = new();

            static EventTableTR()
            {
                RegisterClear(isTR: true, typeof(TDelegate), _events.Clear);
            }

            public static BucketTR<TDelegate> GetOrCreate(string eventName)
            {
                if (!_events.TryGetValue(eventName, out var bucket))
                {
                    bucket = new BucketTR<TDelegate>();
                    _events.Add(eventName, bucket);
                }
                return bucket;
            }

            public static bool TryGet(string eventName, out BucketTR<TDelegate> bucket)
                => _events.TryGetValue(eventName, out bucket);

            public static void RemoveListener(string eventName, string listenerName)
            {
                if (_events.TryGetValue(eventName, out var bucket))
                {
                    bucket.Remove(listenerName);
                    if (!bucket.Has) _events.Remove(eventName);
                }
            }
        }

        #endregion

        #region Enum -> EventName Key (fast + cached)

        private static class EnumEventName
        {
            private static readonly Dictionary<(RuntimeTypeHandle, ulong), string> _cache = new();

            public static string ToKey<TEnum>(TEnum e) where TEnum : unmanaged, Enum
            {
                var th = typeof(TEnum).TypeHandle;
                ulong bits = EnumToUInt64(e);
                var key = (th, bits);

                if (_cache.TryGetValue(key, out var s))
                    return s;

                s = $"{typeof(TEnum).FullName}.{e}";
                _cache[key] = s;
                return s;
            }

            public static void ClearCache() => _cache.Clear();

            private static ulong EnumToUInt64<TEnum>(TEnum value) where TEnum : unmanaged, Enum
            {
                unsafe
                {
                    if (sizeof(TEnum) == 1) return *(byte*)&value;
                    if (sizeof(TEnum) == 2) return *(ushort*)&value;
                    if (sizeof(TEnum) == 4) return *(uint*)&value;
                    if (sizeof(TEnum) == 8) return *(ulong*)&value;
                }
                return 0;
            }
        }

        private static string Key<TEnum>(TEnum evt) where TEnum : unmanaged, Enum => EnumEventName.ToKey(evt);

        #endregion

        #region Core helpers (reduce repetition)

        private static IListener AddCore<TDelegate>(IListener self, string eventName, TDelegate action)
            where TDelegate : Delegate
        {
            if (self == null || action == null) return self;
            var ln = self.ListenerName;

            EventTable<TDelegate>.GetOrCreate(eventName).Add(ln, action);
            Track(ln, eventName, typeof(TDelegate), isTR: false);
            return self;
        }

        private static IListener AddTRCore<TDelegate>(IListener self, string eventName, TDelegate func)
            where TDelegate : Delegate
        {
            if (self == null || func == null) return self;
            var ln = self.ListenerName;

            EventTableTR<TDelegate>.GetOrCreate(eventName).Set(ln, func);
            Track(ln, eventName, typeof(TDelegate), isTR: true);
            return self;
        }

        private static IListener RemoveCore<TDelegate>(IListener self, string eventName, TDelegate action)
            where TDelegate : Delegate
        {
            if (self == null || action == null) return self;
            var ln = self.ListenerName;

            if (EventTable<TDelegate>.TryGet(eventName, out var bucket))
                bucket.Remove(ln, action);

            return self;
        }

        #endregion

        #region AddListener (string / IEventInfo type)

        public static IListener AddListener(this IListener self, string eventName, UnityAction action)
            => AddCore(self, eventName, action);

        public static IListener AddListener<T>(this IListener self, string eventName, UnityAction<T> action)
            => AddCore(self, eventName, action);

        public static IListener AddListener<T, T1>(this IListener self, string eventName, UnityAction<T, T1> action)
            => AddCore(self, eventName, action);

        public static IListener AddListener<T, T1, T2>(this IListener self, string eventName, UnityAction<T, T1, T2> action)
            => AddCore(self, eventName, action);

        #endregion

        #region EventTrigger (string / IEventInfo type)


        public static void EventTrigger(string eventName)
        {
#if UNITY_EDITOR
            Debug_RecordAttempt(eventName);
#endif

            if (!EventTable<UnityAction>.TryGet(eventName, out var bucket) || bucket.Combined == null)
            {
#if UNITY_EDITOR
                Debug_RecordFail(eventName, DebugTriggerResult.Fail_NotFound);
                Debug.LogWarning($"未能找到事件 {eventName}");
#endif
                return;
            }

            try
            {
                bucket.Combined.Invoke();
#if UNITY_EDITOR
                Debug_RecordSuccess(eventName);
#endif
            }
            catch (Exception e)
            {
#if UNITY_EDITOR
                Debug_RecordFail(eventName, DebugTriggerResult.Fail_Exception, e);
#endif
                Debug.LogException(e);
                throw;
            }
        }
        public static void EventTrigger<T>(string eventName, T a0)
        {
#if UNITY_EDITOR
            Debug_RecordAttempt(eventName);
#endif

            if (!EventTable<UnityAction<T>>.TryGet(eventName, out var bucket) || bucket.Combined == null)
            {
#if UNITY_EDITOR
                Debug_RecordFail(eventName, DebugTriggerResult.Fail_NotFound);
                Debug.LogWarning($"未能找到事件 {eventName}");
#endif
                return;
            }

            try
            {
                bucket.Combined.Invoke(a0);
#if UNITY_EDITOR
                Debug_RecordSuccess(eventName);
#endif
            }
            catch (Exception e)
            {
#if UNITY_EDITOR
                Debug_RecordFail(eventName, DebugTriggerResult.Fail_Exception, e);
#endif
                Debug.LogException(e);
                throw;
            }
        }

        public static void EventTrigger<T, T1>(string eventName, T a0, T1 a1)
        {
#if UNITY_EDITOR
            Debug_RecordAttempt(eventName);
#endif

            if (!EventTable<UnityAction<T, T1>>.TryGet(eventName, out var bucket) || bucket.Combined == null)
            {
#if UNITY_EDITOR
                Debug_RecordFail(eventName, DebugTriggerResult.Fail_NotFound);
                Debug.LogWarning($"未能找到事件 {eventName}");
#endif
                return;
            }

            try
            {
                bucket.Combined.Invoke(a0, a1);
#if UNITY_EDITOR
                Debug_RecordSuccess(eventName);
#endif
            }
            catch (Exception e)
            {
#if UNITY_EDITOR
                Debug_RecordFail(eventName, DebugTriggerResult.Fail_Exception, e);
#endif
                Debug.LogException(e);
                throw;
            }
        }

        public static void EventTrigger<T, T1, T2>(string eventName, T a0, T1 a1, T2 a2)
        {
#if UNITY_EDITOR
            Debug_RecordAttempt(eventName);
#endif

            if (!EventTable<UnityAction<T, T1, T2>>.TryGet(eventName, out var bucket) || bucket.Combined == null)
            {
#if UNITY_EDITOR
                Debug_RecordFail(eventName, DebugTriggerResult.Fail_NotFound);
                Debug.LogWarning($"未能找到事件 {eventName}");
#endif
                return;
            }

            try
            {
                bucket.Combined.Invoke(a0, a1, a2);
#if UNITY_EDITOR
                Debug_RecordSuccess(eventName);
#endif
            }
            catch (Exception e)
            {
#if UNITY_EDITOR
                Debug_RecordFail(eventName, DebugTriggerResult.Fail_Exception, e);
#endif
                Debug.LogException(e);
                throw;
            }
        }

        #endregion

        #region RemoveListener (string)

        public static IListener RemoveListener(this IListener self, string eventName, UnityAction action)
            => RemoveCore(self, eventName, action);

        public static IListener RemoveListener<T>(this IListener self, string eventName, UnityAction<T> action)
            => RemoveCore(self, eventName, action);

        public static IListener RemoveListener<T, T1>(this IListener self, string eventName, UnityAction<T, T1> action)
            => RemoveCore(self, eventName, action);

        public static IListener RemoveListener<T, T1, T2>(this IListener self, string eventName, UnityAction<T, T1, T2> action)
            => RemoveCore(self, eventName, action);

        #endregion

        #region AddListener_TR / EventTrigger_TR (string)

        public static IListener AddListener_TR<TResult>(this IListener self, string eventName, Func<TResult> func)
            => AddTRCore(self, eventName, func);

        public static IListener AddListener_TR<T, TResult>(this IListener self, string eventName, Func<T, TResult> func)
            => AddTRCore(self, eventName, func);

        public static IListener AddListener_TR<T, T1, TResult>(this IListener self, string eventName, Func<T, T1, TResult> func)
            => AddTRCore(self, eventName, func);

        public static IListener AddListener_TR<T, T1, T2, TResult>(this IListener self, string eventName, Func<T, T1, T2, TResult> func)
            => AddTRCore(self, eventName, func);

        public static TResult EventTrigger_TR<TResult>(string eventName)
        {
#if UNITY_EDITOR
            Debug_RecordAttempt(eventName);
#endif

            if (!EventTableTR<Func<TResult>>.TryGet(eventName, out var bucket))
            {
#if UNITY_EDITOR
                Debug_RecordFail(eventName, DebugTriggerResult.Fail_NotFound);
                Debug.LogWarning($"未能找到TR事件 {eventName}");
#endif
                return default;
            }

            if (bucket.Func == null)
            {
#if UNITY_EDITOR
                Debug_RecordFail(eventName, DebugTriggerResult.Fail_NullDelegate);
                Debug.LogWarning($"TR事件 {eventName} 的委托为空（Func 为 null）");
#endif
                return default;
            }

            try
            {
                var ret = bucket.Func.Invoke();
#if UNITY_EDITOR
                Debug_RecordSuccess(eventName);
#endif
                return ret;
            }
            catch (Exception e)
            {
#if UNITY_EDITOR
                Debug_RecordFail(eventName, DebugTriggerResult.Fail_Exception, e);
#endif
                Debug.LogException(e);
                throw;
            }
        }

        public static TResult EventTrigger_TR<T, TResult>(string eventName, T a0)
        {
#if UNITY_EDITOR
            Debug_RecordAttempt(eventName);
#endif

            if (!EventTableTR<Func<T, TResult>>.TryGet(eventName, out var bucket))
            {
#if UNITY_EDITOR
                Debug_RecordFail(eventName, DebugTriggerResult.Fail_NotFound);
                Debug.LogWarning($"未能找到TR事件 {eventName}");
#endif
                return default;
            }

            if (bucket.Func == null)
            {
#if UNITY_EDITOR
                Debug_RecordFail(eventName, DebugTriggerResult.Fail_NullDelegate);
                Debug.LogWarning($"TR事件 {eventName} 的委托为空（Func 为 null）");
#endif
                return default;
            }

            try
            {
                var ret = bucket.Func.Invoke(a0);
#if UNITY_EDITOR
                Debug_RecordSuccess(eventName);
#endif
                return ret;
            }
            catch (Exception e)
            {
#if UNITY_EDITOR
                Debug_RecordFail(eventName, DebugTriggerResult.Fail_Exception, e);
                Debug.LogException(e);
#endif
                return default;
            }
        }

        public static TResult EventTrigger_TR<T, T1, TResult>(string eventName, T a0, T1 a1)
        {
#if UNITY_EDITOR
            Debug_RecordAttempt(eventName);
#endif

            if (!EventTableTR<Func<T, T1, TResult>>.TryGet(eventName, out var bucket))
            {
#if UNITY_EDITOR
                Debug_RecordFail(eventName, DebugTriggerResult.Fail_NotFound);
                Debug.LogWarning($"未能找到TR事件 {eventName}");
#endif
                return default;
            }

            if (bucket.Func == null)
            {
#if UNITY_EDITOR
                Debug_RecordFail(eventName, DebugTriggerResult.Fail_NullDelegate);
                Debug.LogWarning($"TR事件 {eventName} 的委托为空（Func 为 null）");
#endif
                return default;
            }

            try
            {
                var ret = bucket.Func.Invoke(a0, a1);
#if UNITY_EDITOR
                Debug_RecordSuccess(eventName);
#endif
                return ret;
            }
            catch (Exception e)
            {
#if UNITY_EDITOR
                Debug_RecordFail(eventName, DebugTriggerResult.Fail_Exception, e);
                Debug.LogException(e);
#endif
                return default;
            }
        }

        public static TResult EventTrigger_TR<T, T1, T2, TResult>(string eventName, T a0, T1 a1, T2 a2)
        {
#if UNITY_EDITOR
            Debug_RecordAttempt(eventName);
#endif

            if (!EventTableTR<Func<T, T1, T2, TResult>>.TryGet(eventName, out var bucket))
            {
#if UNITY_EDITOR
                Debug_RecordFail(eventName, DebugTriggerResult.Fail_NotFound);
                Debug.LogWarning($"未能找到TR事件 {eventName}");
#endif
                return default;
            }

            if (bucket.Func == null)
            {
#if UNITY_EDITOR
                Debug_RecordFail(eventName, DebugTriggerResult.Fail_NullDelegate);
                Debug.LogWarning($"TR事件 {eventName} 的委托为空（Func 为 null）");
#endif
                return default;
            }

            try
            {
                var ret = bucket.Func.Invoke(a0, a1, a2);
#if UNITY_EDITOR
                Debug_RecordSuccess(eventName);
#endif
                return ret;
            }
            catch (Exception e)
            {
#if UNITY_EDITOR
                Debug_RecordFail(eventName, DebugTriggerResult.Fail_Exception, e);
                Debug.LogException(e);
#endif
                return default;
            }
        }
        #endregion

        #region RemoveOne / RemoveALL

        public static IListener RemoveOne_ThisObjListenter(this IListener self, string eventName)
        {
            if (self == null) return null;
            var ln = self.ListenerName;

            if (_listenerRegs.TryGetValue(ln, out var set))
            {
                var toRemove = new List<RegKey>();
                foreach (var k in set)
                    if (k.EventName == eventName)
                        toRemove.Add(k);

                for (int i = 0; i < toRemove.Count; i++)
                {
                    var k = toRemove[i];
                    var remover = GetRemover(k.DelegateType);
                    if (k.IsTR) remover.RemoveTR(k.EventName, ln);
                    else remover.Remove(k.EventName, ln);

                    set.Remove(k);
                }

                if (set.Count == 0) _listenerRegs.Remove(ln);
            }
#if UNITY_EDITOR
            else Debug.LogWarning($"RemoveOne: listener={ln} 没有注册记录");
#endif
            return self;
        }

        public static IListener RemoveALL_ThisObjListenter(this IListener self)
        {
            if (self == null) return null;

            var ln = self.ListenerName;

            if (_listenerRegs.TryGetValue(ln, out var set))
            {
                foreach (var k in set)
                {
                    var remover = GetRemover(k.DelegateType);
                    if (k.IsTR) remover.RemoveTR(k.EventName, ln);
                    else remover.Remove(k.EventName, ln);
                }
                _listenerRegs.Remove(ln);
            }

            if (self is IOnlyOneID oneID)
                oneID.ReleaseID();

            return self;
        }

        #endregion

        #region UnRegister (requires your components ClearActionsOnDestroy/ClearActionsOnDisable)

        public static IListener UnRegisterWhenDestroyed(this IListener self)
        {
            if (self is MonoBehaviour mono)
            {
                var c = mono.GetComponent<ClearActionsOnDestroy>();
                if (c == null) c = mono.gameObject.AddComponent<ClearActionsOnDestroy>();
                c.OnInit(self);
            }
            return self;
        }

        public static IListener UnRegisterWhenDisabled(this IListener self)
        {
            if (self is MonoBehaviour mono)
            {
                var c = mono.GetComponent<ClearActionsOnDisable>();
                if (c == null) c = mono.gameObject.AddComponent<ClearActionsOnDisable>();
                c.OnInit(self);
            }
            return self;
        }

        #endregion

        #region Clear

        public static void Clear()
        {
            // Clear all used generic signature tables
            for (int i = 0; i < _clearAll.Count; i++)
                _clearAll[i]?.Invoke();

            _listenerRegs.Clear();
            _removers.Clear();
            EnumEventName.ClearCache();
            OnlyOneIdPool.Clear();
        }

        #endregion

        #region Enum overloads (same name / same params but enum)

        // AddListener
        public static IListener AddListener<TEnum>(this IListener self, TEnum evt, UnityAction action)
            where TEnum : unmanaged, Enum
            => AddListener(self, Key(evt), action);

        public static IListener AddListener<TEnum, T>(this IListener self, TEnum evt, UnityAction<T> action)
            where TEnum : unmanaged, Enum
            => AddListener(self, Key(evt), action);

        public static IListener AddListener<TEnum, T, T1>(this IListener self, TEnum evt, UnityAction<T, T1> action)
            where TEnum : unmanaged, Enum
            => AddListener(self, Key(evt), action);

        public static IListener AddListener<TEnum, T, T1, T2>(this IListener self, TEnum evt, UnityAction<T, T1, T2> action)
            where TEnum : unmanaged, Enum
            => AddListener(self, Key(evt), action);

        // EventTrigger
        public static void EventTrigger<TEnum>(TEnum evt)
            where TEnum : unmanaged, Enum
            => EventTrigger(Key(evt));

        public static void EventTrigger<TEnum, T>(TEnum evt, T a0)
            where TEnum : unmanaged, Enum
            => EventTrigger(Key(evt), a0);

        public static void EventTrigger<TEnum, T, T1>(TEnum evt, T a0, T1 a1)
            where TEnum : unmanaged, Enum
            => EventTrigger(Key(evt), a0, a1);

        public static void EventTrigger<TEnum, T, T1, T2>(TEnum evt, T a0, T1 a1, T2 a2)
            where TEnum : unmanaged, Enum
            => EventTrigger(Key(evt), a0, a1, a2);

        // RemoveListener
        public static IListener RemoveListener<TEnum>(this IListener self, TEnum evt, UnityAction action)
            where TEnum : unmanaged, Enum
            => RemoveListener(self, Key(evt), action);

        public static IListener RemoveListener<TEnum, T>(this IListener self, TEnum evt, UnityAction<T> action)
            where TEnum : unmanaged, Enum
            => RemoveListener(self, Key(evt), action);

        public static IListener RemoveListener<TEnum, T, T1>(this IListener self, TEnum evt, UnityAction<T, T1> action)
            where TEnum : unmanaged, Enum
            => RemoveListener(self, Key(evt), action);

        public static IListener RemoveListener<TEnum, T, T1, T2>(this IListener self, TEnum evt, UnityAction<T, T1, T2> action)
            where TEnum : unmanaged, Enum
            => RemoveListener(self, Key(evt), action);

        // AddListener_TR (Query)
        public static IListener AddListener_TR<TEnum, TResult>(this IListener self, TEnum evt, Func<TResult> func)
            where TEnum : unmanaged, Enum
            => AddListener_TR(self, Key(evt), func);

        public static IListener AddListener_TR<TEnum, T, TResult>(this IListener self, TEnum evt, Func<T, TResult> func)
            where TEnum : unmanaged, Enum
            => AddListener_TR(self, Key(evt), func);

        public static IListener AddListener_TR<TEnum, T, T1, TResult>(this IListener self, TEnum evt, Func<T, T1, TResult> func)
            where TEnum : unmanaged, Enum
            => AddListener_TR(self, Key(evt), func);

        public static IListener AddListener_TR<TEnum, T, T1, T2, TResult>(this IListener self, TEnum evt, Func<T, T1, T2, TResult> func)
            where TEnum : unmanaged, Enum
            => AddListener_TR(self, Key(evt), func);

        // EventTrigger_TR (Query)
        public static TResult EventTrigger_TR<TEnum, TResult>(TEnum evt)
            where TEnum : unmanaged, Enum
            => EventTrigger_TR<TResult>(Key(evt));
        public static TResult EventTrigger_TR<TEnum, T, TResult>(TEnum evt, T a0)
            where TEnum : unmanaged, Enum
            => EventTrigger_TR<T, TResult>(Key(evt), a0);

        public static TResult EventTrigger_TR<TEnum, T, T1, TResult>(TEnum evt, T a0, T1 a1)
            where TEnum : unmanaged, Enum
            => EventTrigger_TR<T, T1, TResult>(Key(evt), a0, a1);

        public static TResult EventTrigger_TR<TEnum, T, T1, T2, TResult>(TEnum evt, T a0, T1 a1, T2 a2)
            where TEnum : unmanaged, Enum
            => EventTrigger_TR<T, T1, T2, TResult>(Key(evt), a0, a1, a2);

        public static TResult EventTrigger_TR<TResult>(EventEnumType evt)=> EventTrigger_TR<TResult>(Key(evt));
        public static TResult EventTrigger_TR< T, TResult>(EventEnumType evt, T a0) => EventTrigger_TR<T, TResult>(Key(evt), a0);
        public static TResult EventTrigger_TR<T, T1, TResult>(EventEnumType evt, T a0, T1 a1)=> EventTrigger_TR<T, T1, TResult>(Key(evt), a0, a1);
        public static TResult EventTrigger_TR< T, T1, T2, TResult>(EventEnumType evt, T a0, T1 a1, T2 a2)=> EventTrigger_TR<T, T1, T2, TResult>(Key(evt), a0, a1, a2);

        // RemoveOne overload (enum)
        public static IListener RemoveOne_ThisObjListenter<TEnum>(this IListener self, TEnum evt)
            where TEnum : unmanaged, Enum
            => RemoveOne_ThisObjListenter(self, Key(evt));

        #endregion

#if UNITY_EDITOR
        // ================== 统一 Debug 统计（Attempt/Success/Fail + TriggerCount/LastTrigger） ==================

        internal enum DebugTriggerResult
        {
            Success = 0,
            Fail_NotFound = 1,
            Fail_NullDelegate = 2,
            Fail_Exception = 3,
        }

        private struct DebugTriggerStat
        {
            public int attempt;
            public int success;
            public int fail;

            public int triggerCount;
            public double lastTriggerTime;

            public double lastAttemptTime;
            public double lastSuccessTime;
            public double lastFailTime;

            public DebugTriggerResult lastFailReason;
            public string lastException;
        }

        private static readonly Dictionary<string, DebugTriggerStat> _debugTriggerStats = new(256);

        private static double Debug_Now() => EditorApplication.timeSinceStartup;

        internal static void Debug_RecordAttempt(string eventName)
        {
            if (string.IsNullOrEmpty(eventName)) return;

            if (!_debugTriggerStats.TryGetValue(eventName, out var s)) s = default;
            s.attempt++;
            s.lastAttemptTime = Debug_Now();
            _debugTriggerStats[eventName] = s;
        }

        internal static void Debug_RecordSuccess(string eventName)
        {
            if (string.IsNullOrEmpty(eventName)) return;

            if (!_debugTriggerStats.TryGetValue(eventName, out var s)) s = default;
            s.success++;
            s.triggerCount++;

            var now = Debug_Now();
            s.lastSuccessTime = now;
            s.lastTriggerTime = now;

            // ✅ 清掉旧异常信息，避免窗口一直显示旧异常
            s.lastException = null;
            s.lastFailReason = DebugTriggerResult.Success;

            _debugTriggerStats[eventName] = s;
        }

        internal static void Debug_RecordFail(string eventName, DebugTriggerResult reason, Exception ex = null)
        {
            if (string.IsNullOrEmpty(eventName)) return;

            if (!_debugTriggerStats.TryGetValue(eventName, out var s)) s = default;
            s.fail++;
            s.lastFailTime = Debug_Now();
            s.lastFailReason = reason;

            if (ex != null)
            {
                var msg = ex.GetType().Name + ": " + ex.Message;
                s.lastException = msg.Length > 200 ? msg.Substring(0, 200) : msg;
            }
            else s.lastException = null;

            _debugTriggerStats[eventName] = s;
        }

        // 给 DebugWindow 用（你窗口里已在用）
        internal static bool Debug_TryGetTriggerStat(
            string eventName,
            out int attempt,
            out int success,
            out int fail,
            out double lastFailTime,
            out int lastFailReason,
            out string lastException)
        {
            if (_debugTriggerStats.TryGetValue(eventName, out var s))
            {
                attempt = s.attempt;
                success = s.success;
                fail = s.fail;
                lastFailTime = s.lastFailTime;
                lastFailReason = (int)s.lastFailReason;
                lastException = s.lastException;
                return true;
            }

            attempt = success = fail = 0;
            lastFailTime = 0;
            lastFailReason = 0;
            lastException = null;
            return false;
        }

        // ================== EDITOR DEBUG API（注册枚举/移除/添加调试监听） ==================

        public readonly struct DebugReg
        {
            public readonly string EventName;
            public readonly string ListenerName;
            public readonly Type DelegateType;
            public readonly bool IsTR;

            public DebugReg(string eventName, string listenerName, Type delegateType, bool isTR)
            {
                EventName = eventName;
                ListenerName = listenerName;
                DelegateType = delegateType;
                IsTR = isTR;
            }

            public string DelegateTypeName => DelegateType != null ? DelegateType.Name : "NULL";
        }

        public static List<DebugReg> Debug_GetAllRegs()
        {
            var list = new List<DebugReg>(256);
            foreach (var kv in _listenerRegs) // listenerName -> set(RegKey)
            {
                var listenerName = kv.Key;
                var set = kv.Value;
                foreach (var k in set)
                    list.Add(new DebugReg(k.EventName, listenerName, k.DelegateType, k.IsTR));
            }
            return list;
        }

        public static bool Debug_RemoveOneReg(string eventName, string listenerName, Type delegateType, bool isTR)
        {
            if (string.IsNullOrEmpty(eventName) || string.IsNullOrEmpty(listenerName) || delegateType == null)
                return false;

            var remover = GetRemover(delegateType);
            if (isTR) remover.RemoveTR(eventName, listenerName);
            else remover.Remove(eventName, listenerName);

            if (_listenerRegs.TryGetValue(listenerName, out var set))
            {
                RegKey toRemove = default;
                bool found = false;

                foreach (var k in set)
                {
                    if (k.EventName == eventName && k.DelegateType == delegateType && k.IsTR == isTR)
                    {
                        toRemove = k;
                        found = true;
                        break;
                    }
                }

                if (found) set.Remove(toRemove);
                if (set.Count == 0) _listenerRegs.Remove(listenerName);
            }

            return true;
        }

        public static int Debug_RemoveListenerFromEvent(string eventName, string listenerName)
        {
            if (string.IsNullOrEmpty(eventName) || string.IsNullOrEmpty(listenerName))
                return 0;

            int removed = 0;

            if (_listenerRegs.TryGetValue(listenerName, out var set))
            {
                // 用 List 缓存避免遍历时修改 set
                var toRemove = new List<RegKey>(8);
                foreach (var k in set)
                    if (k.EventName == eventName) toRemove.Add(k);

                for (int i = 0; i < toRemove.Count; i++)
                {
                    var k = toRemove[i];
                    var remover = GetRemover(k.DelegateType);
                    if (k.IsTR) remover.RemoveTR(k.EventName, listenerName);
                    else remover.Remove(k.EventName, listenerName);

                    set.Remove(k);
                    removed++;
                }

                if (set.Count == 0) _listenerRegs.Remove(listenerName);
            }

            return removed;
        }

        public static int Debug_RemoveEvent(string eventName)
        {
            if (string.IsNullOrEmpty(eventName)) return 0;

            int removed = 0;

            // 先收集所有关联 listener，避免遍历字典时修改
            var listeners = new List<string>(64);
            foreach (var kv in _listenerRegs)
            {
                foreach (var k in kv.Value)
                {
                    if (k.EventName == eventName)
                    {
                        listeners.Add(kv.Key);
                        break;
                    }
                }
            }

            for (int i = 0; i < listeners.Count; i++)
                removed += Debug_RemoveListenerFromEvent(eventName, listeners[i]);

            return removed;
        }

        private sealed class EditorDebugListener : IListener
        {
            public string Name;
            public string ListenerName => Name;
        }

        private static readonly Dictionary<string, EditorDebugListener> _editorDebugListeners = new();

        public static void Debug_AddLogListener(string eventName, string listenerName, string logPrefix = "[EventCenterDebug]")
        {
            if (string.IsNullOrEmpty(eventName)) return;
            if (string.IsNullOrEmpty(listenerName)) listenerName = $"EditorDebug_{Guid.NewGuid():N}".Substring(0, 16);

            if (!_editorDebugListeners.TryGetValue(listenerName, out var l))
            {
                l = new EditorDebugListener { Name = listenerName };
                _editorDebugListeners.Add(listenerName, l);
            }

            UnityAction action = () => Debug.Log($"{logPrefix} {eventName} triggered -> {listenerName}");
            AddCore<UnityAction>(l, eventName, action);
        }
#endif
    }
}