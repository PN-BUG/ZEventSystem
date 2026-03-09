using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ZEventSystem;

public class EventCenterTest : MonoBehaviour, IListener, IOnlyOneID
{
    public int Rid { get; set; }

    public string eventName = "EventCenterTest";
    public string eventName2 = "EventCenterTest_TR";
    private void OnEnable()
    {
        this.AddListener(eventName, EventCenterTestDo)  // 注册无返回值事件（一对多），事件名可用字符串或枚举
            .UnRegisterWhenDestroyed();//销毁时自动解绑，不推荐使用 增加一个mono脚本实现，增加性能开销，推荐使用 RemoveALL_ThisObjListenter 手动解绑所有事件，避免忘记解绑导致的内存泄漏

        // 注册有返回值事件（一对一），TResult是返回值类型，事件名可用字符串或枚举
        this.AddListener_TR<bool>(eventName2, EventCenterTestDo_TR);

        // 触发事件，参数类型必须和注册时一致，否则会报错
        EventCenter.EventTrigger(eventName);
        bool resultA = EventCenter.EventTrigger_TR<bool>(eventName2);
        EventCenter.EventTrigger(EventEnumType.EventCenterTest, 100, "ABC");

        //使用任意 Enum
        bool resultB = EventCenter.EventTrigger_TR<EventEnumType, bool>(EventEnumType.EventCenterTest);
        //使用 EventEnumType
        bool resultC = EventCenter.EventTrigger_TR<bool>(EventEnumType.EventCenterTest);
    }

    private void OnDisable()
    {
        // 可选：如果你没有用 ClearActionsOnDisable/IListener 引用那套，也可以手动释放ID
        //this.ReleaseID();
        this.RemoveALL_ThisObjListenter();//手动解绑所有事件，推荐使用，避免忘记解绑导致的内存泄漏
    }

    void EventCenterTestDo()
    {
        Debug.Log($"EventCenterTestDo: {name} evt={eventName} rid={Rid}");
    }
    bool EventCenterTestDo_TR()
    {
        Debug.Log($"EventCenterTestDo_TR: {name} evt={eventName} rid={Rid}");
        return true;
    }
    void RemoveOneEvent() {
        this.RemoveOne_ThisObjListenter(eventName);//手动解绑单个事件
    }
}