using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Events;
namespace ZEventSystem
{
    public interface IListener
    {
        string ListenerName
        {
            get
            {
                if (this is IOnlyOneID)
                    return GetType().Name + (this as IOnlyOneID).GetID();
                else return GetType().Name;
            }
        }
    }
    public interface IOnlyOneID
    {
        static SortedSet<int> UseIDList = new SortedSet<int>();
        static Queue<int> UnUseIDList = new Queue<int>(); // 先进先出
        int Rid { get; set; }

        public static void Clear()
        {
            UseIDList = new();
            UnUseIDList = new();
        }
    }

    #region  结构体类型事件封装  演示
    public struct IdChangeEvent : IEventInfo { public int a; }
    public struct IdChangeEvent<T> : IEventInfo
    {
        public int a;
        public UnityAction<T> actions;
        public IdChangeEvent(int a, UnityAction<T> action)
        {
            this.a = a;
            actions = action;
        }
    }
    public struct NewSceneEvent : IEventInfo { }

    public struct HideTipsEvent : IEventInfo { }

    //触发 演示
    //EventCenter.EventTrigger<HideTipsEvent>();
    #endregion
    public interface IEventInfo { }
    /// <summary>
    /// 事件中心
    /// （定义的事件不能重名 否则会认错
    /// </summary>
    public static class EventCenter
    {
        #region EventInfo....
        // 定义一个通用的事件信息类，能够处理不同数量参数的 Action 和 Func
        public class EventInfo<TDelegate> : IEventInfo where TDelegate : Delegate
        {
            public TDelegate Actions { get; set; }
            public EventInfo(TDelegate action)
            {
                // actions += action;
                Actions = Delegate.Combine(Actions, action) as TDelegate;
            }
        }
        //使用 Delegate.Combine 替代 += 是一种处理泛型委托的通用方法，Delegate.Remove 可以用于实现 -= 的行为。
        //一些要注意的地方：
        //Delegate.Combine 和 Delegate.Remove 都返回 Delegate 类型，因此需要将结果显式地转换回泛型委托类型 TDelegate。
        //Delegate.Combine 可以接受 null 值作为参数，如果 Actions 或 action 为 null，它依然可以工作。返回结果将是非 null 委托，除非两个参数都是 null。
        //eventInfo.actions?.DynamicInvoke();
        #endregion
        /// <summary>
        /// 事件名 < 监听对象名 , 对象监听事件函数 >
        /// </summary>
        private static Dictionary<string, Dictionary<string, EventInfo<Delegate>>> eventDic = new();

#if UNITY_EDITOR
        /// <summary>
        /// 新方法使用示例:
        /// </summary>
        static void NewFunTest()
        {
            //使用示例:

            // 对于无参数且无返回值的函数
            var eventInfo = new EventInfo<UnityAction>(() => Debug.Log("Action Invoked"));

            // 对于带有一个参数且无返回值的函数
            var eventInfo1Param = new EventInfo<UnityAction<int>>(x => Debug.Log($"Action with int: {x}"));

            // 对于带有2个参数且无返回值的函数
            var eventInfo2Param = new EventInfo<UnityAction<int, string>>((x, y) => Debug.Log($"Action with int: {x} and string: {y}"));

            // 使用Func委托，带有返回值的委托
            var eventInfoWithReturn = new EventInfo<Func<int, int>>(x => x * 2);
        }
#endif
        #region IOnlyOneID
        private static readonly object lockObj = new();//多线程访问时可能会有问题，如果多个线程同时调用 Init()，可能会导致 ID 竞争
        public static int Init(this IOnlyOneID self)
        {
            lock (lockObj)
            {
                self.ReleaseID();
                if (IOnlyOneID.UnUseIDList.Count > 0)
                {
                    self.Rid = IOnlyOneID.UnUseIDList.Dequeue();
                }
                else
                {
                    self.Rid = (IOnlyOneID.UseIDList.Count > 0) ? IOnlyOneID.UseIDList.Max + 1 : 1;
                }
                IOnlyOneID.UseIDList.Add(self.Rid);
                return self.Rid;
            }
        }
        public static void TryInit(this IOnlyOneID self)
        {
            if (self.Rid != 0) return;
            Init(self);
        }
        public static int GetID(this IOnlyOneID self)
        {
            if (self.Rid != 0) return self.Rid;
            return Init(self);
        }
        public static void ReleaseID(this IOnlyOneID self)
        {
            if (self.Rid == 0) return;
            var id = self.Rid;
            if (IOnlyOneID.UseIDList.Remove(id))
                IOnlyOneID.UnUseIDList.Enqueue(id);//IOnlyOneID.UnUseIDList.Add(id);
            self.Rid = 0;
        }

        #endregion
        #region 无返回值  一对多 / 多对多
        /// <summary>
        /// 定义的事件不能同名 否则会认错
        /// </summary>
        public static IListener AddListener<T>(this IListener self, UnityAction action) where T : IEventInfo
        {
            return AddListener(self, typeof(T).ToString(), action);
        }
        public static IListener AddListener(this IListener self, string actionsName, UnityAction action) => AddListener<UnityAction>(self, actionsName, action);
        public static IListener AddListener<T>(this IListener self, string actionsName, UnityAction<T> action) => AddListener<UnityAction<T>>(self, actionsName, action);

        /// <summary>
        ///添加到 事件+对象名的监听事件集合 当对象销毁时 移除监听事件集合
        /// </summary>
        /// <param name="name">事件+对象名</param>
        /// <param name="action"></param>
        /// <param name="gameObject"></param>
        /// 
        public static IListener AddListener<T, T1>(this IListener self, string actionsName, UnityAction<T, T1> action) => AddListener<UnityAction<T, T1>>(self, actionsName, action);
        public static IListener AddListener<T, T1, T2>(this IListener self, string actionsName, UnityAction<T, T1, T2> action) => AddListener<UnityAction<T, T1, T2>>(self, actionsName, action);
        public static IListener AddListener<TDelegate>(this IListener self, string actionsName, TDelegate action) where TDelegate : Delegate
        {
            if (!eventDic.TryGetValue(actionsName, out var actionDict))
            {
                actionDict = new Dictionary<string, EventInfo<Delegate>>();
                eventDic.Add(actionsName, actionDict);
            }

            var eventName = self.ListenerName;
            if (!actionDict.TryGetValue(eventName, out var eventInfo))
            {
                eventInfo = new EventInfo<Delegate>(action);
                actionDict.Add(eventName, eventInfo);
            }
            else
                eventInfo.Actions = Delegate.Combine(eventInfo.Actions, action);

            return self;
        }

        public static void EventTrigger<T>() => EventTrigger(typeof(T).ToString());
        public static void EventTrigger(string name) => EventTrigger<object, object, object>(name, null, null, null);
        public static void EventTrigger<T>(string name, T info) => EventTrigger<T, object, object>(name, info, null, null);
        public static void EventTrigger<T, T1>(string name, T info, T1 info1) => EventTrigger<T, T1, object>(name, info, info1, null);
        public static void EventTrigger<T, T1, T2>(string actionsName, T info, T1 info1, T2 info2)
        {
            if (!eventDic.TryGetValue(actionsName, out var actionDict))
            {
                Debug.LogWarning($"未能找到事件{actionsName}");
                return;
            }
            if (info == null)
            {
                //foreach (var eventInfo in actionDict.Values)
                //eventInfo.Actions?.DynamicInvoke();
                for (int i = 0; i < actionDict.Values.Count; i++)
                {
                    try
                    {
                        var eventInfo = actionDict.Values.ElementAt(i);
                        eventInfo.Actions?.DynamicInvoke();
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"{e} {e.Message}");
                    }
                }
            }
            else if (info1 == null)
            {
                //foreach (var eventInfo in actionDict.Values)
                //    eventInfo.Actions?.DynamicInvoke(info);
                for (int i = 0; i < actionDict.Values.Count; i++)
                {
                    var eventInfo = actionDict.Values.ElementAt(i);
                    eventInfo.Actions?.DynamicInvoke(info);
                    //try
                    //{
                    //    eventInfo.Actions?.DynamicInvoke(info);
                    //}
                    //catch (Exception ex)
                    //{
                    //    Debug.LogError($"Exception during event invocation: {ex.Message}\n{ex.StackTrace}");
                    //    if (ex.InnerException != null)
                    //    {
                    //        Debug.LogError($"Inner Exception: {ex.InnerException.Message}\n{ex.InnerException.StackTrace}");
                    //    }
                    //}
                }
            }
            else if (info2 == null)
            {
                for (int i = 0; i < actionDict.Values.Count; i++)
                {
                    var eventInfo = actionDict.Values.ElementAt(i);
                    eventInfo.Actions?.DynamicInvoke(info, info1);
                }
                //foreach (var eventInfo in actionDict.Values)
                //    eventInfo.Actions?.DynamicInvoke(info, info1);
            }
            else
            {
                for (int i = 0; i < actionDict.Values.Count; i++)
                {
                    try
                    {
                        var eventInfo = actionDict.Values.ElementAt(i);
                        eventInfo.Actions?.DynamicInvoke(info, info1, info2);
                    }
                    catch (Exception e)
                    {
                        Debug.LogException(e);
                    }
                }
                //foreach (var eventInfo in actionDict.Values)
                //    eventInfo.Actions?.DynamicInvoke(info, info1, info2);
            }
        }

        #region 有返回值 一对一
        public static IListener AddListener_TR<TResult>(this IListener self, string actionsName, Func<TResult> action) => AddListener_TR<Func<TResult>>(self, actionsName, action);
        public static IListener AddListener_TR<T, TResult>(this IListener self, string actionsName, Func<T, TResult> action) => AddListener_TR<Func<T, TResult>>(self, actionsName, action);
        public static IListener AddListener_TR<T, T1, TResult>(this IListener self, string actionsName, Func<T, T1, TResult> action) => AddListener_TR<Func<T, T1, TResult>>(self, actionsName, action);
        public static IListener AddListener_TR<T, T1, T2, TResult>(this IListener self, string actionsName, Func<T, T1, T2, TResult> action) => AddListener_TR<Func<T, T1, T2, TResult>>(self, actionsName, action);
        public static IListener AddListener_TR<TDelegate>(this IListener self, string actionsName, TDelegate action) where TDelegate : Delegate
        {
            if (!eventDic.TryGetValue(actionsName, out var actionDict))
            {
                actionDict = new Dictionary<string, EventInfo<Delegate>>();
                eventDic.Add(actionsName, actionDict);
            }

            var eventObjName = self.ListenerName;
            if (!actionDict.TryGetValue(eventObjName, out var eventInfo))
            {
                eventInfo = new EventInfo<Delegate>(action);
                actionDict.Add(eventObjName, eventInfo);
            }
            else
                eventInfo.Actions = Delegate.Combine(eventInfo.Actions, action);
            return self;
        }

        public static TResult EventTrigger_TR<TResult>(string name) => EventTrigger_TR<object, object, object, TResult>(name, null, null, null);
        public static TResult EventTrigger_TR<T, TResult>(string name, T info) => EventTrigger_TR<T, object, object, TResult>(name, info, null, null);
        public static TResult EventTrigger_TR<T, T1, TResult>(string name, T info, T1 info1) => EventTrigger_TR<T, T1, object, TResult>(name, info, info1, null);
        public static TResult EventTrigger_TR<T, T1, T2, TResult>(string actionsName, T info, T1 info1, T2 info2)
        {
            if (!eventDic.TryGetValue(actionsName, out var actionDict))
            {
                Debug.LogWarning($"未能找到事件{actionsName}");
                return default;
            }
            foreach (var eventInfo in actionDict.Values)
            {
                if (info == null)
                {
                    return (TResult)eventInfo.Actions?.DynamicInvoke();
                }
                else if (info1 == null)
                {
                    return (TResult)eventInfo.Actions?.DynamicInvoke(info);
                }
                else if (info2 == null)
                {
                    return (TResult)eventInfo.Actions?.DynamicInvoke(info, info1);
                }
                else
                {
                    try
                    {
                        return (TResult)eventInfo.Actions?.DynamicInvoke(info, info1, info2);
                    }
                    catch (Exception e)
                    {
                        Debug.LogException(e);
                    }
                }
            }
            Debug.LogWarning($"未能找到事件{actionsName}");
            return default;
        }
        #endregion
        /// <summary>
        /// 把自己的某个方法从某个事件中移除
        /// </summary>
        /// <typeparam name="TDelegate"></typeparam>
        /// <param name="self"></param>
        /// <param name="actionsName"></param>
        /// <param name="action"></param>
        /// <returns></returns>
        public static IListener RemoveListener<TDelegate>(this IListener self, string actionsName, TDelegate action)
        where TDelegate : Delegate
        {
            if (eventDic.TryGetValue(actionsName, out var actionDict))
            {
                var eventName = self.ListenerName;
                if (actionDict.TryGetValue(eventName, out var eventInfo))
                {
                    eventInfo.Actions = Delegate.Remove(eventInfo.Actions, action);
                }
            }
            return self;
        }
        /// <summary>
        ///  把自己从某个事件中移除
        /// </summary>
        /// <param name="self"></param>
        /// <param name="actionsName"></param>
        /// <returns></returns>
        public static IListener RemoveOne_ThisObjListenter(this IListener self, string actionsName)
        {
            if (eventDic.TryGetValue(actionsName, out Dictionary<string, EventInfo<Delegate>> value)
                && value.ContainsKey(self.ListenerName))
                value.Remove(self.ListenerName);
            else
                Debug.LogWarning($"未能找到事件{actionsName}");
            return self;
        }
        /// <summary>
        /// 把自己从所有事件中移除
        /// </summary>
        /// <param name="self"></param>
        /// <returns></returns>
        public static IListener RemoveALL_ThisObjListenter(this IListener self)
        {
            if (self == null)
            {
                Debug.LogError("IAddEventAction == null");
                return null;
            }
            try
            {
                ClearGameObjectActions(self.ListenerName);
                if (self is IOnlyOneID oneID)
                    oneID.ReleaseID();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }

            return self;
        }

        #endregion

        public static void ClearGameObjectActions(string objName)
        {
            foreach (var item in eventDic)//遍历所有事件
            {
                //查找字典中 objName监听的事件
                if (item.Value.ContainsKey(objName))
                {
                    item.Value.Remove(objName);
                }
            }
        }

        public static IListener UnRegisterWhenDestroyed(this IListener self)
        {
            //创建了一个mono脚本（把这个对象的所有销毁的逻辑交给这个脚本执行）
            if (self is MonoBehaviour mono)
                mono.gameObject.AddComponent<ClearActionsOnDestroy>().OnInit(self.ListenerName);
            return self;
        }

        public static IListener UnRegisterWhenDisabled(this IListener self)
        {
            //创建了一个mono脚本（把这个对象的所有销毁的逻辑交给这个脚本执行）
            if (self is MonoBehaviour mono) mono.gameObject.AddComponent<ClearActionsOnDisable>().OnInit(self.ListenerName);
            return self;
        }

        /// <summary>
        /// 清空事件中心
        /// </summary>
        public static void Clear()
        {
            eventDic.Clear();
            IOnlyOneID.Clear();
        }
    }
}
