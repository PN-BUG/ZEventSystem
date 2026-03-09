using UnityEngine;
using ZEventSystem;

public class ClearActionsOnDisable : MonoBehaviour
{
    private IListener _listener;

    private void OnDisable()
    {
        // 清掉该 listener 注册过的所有事件（O(注册数)）
        _listener?.RemoveALL_ThisObjListenter();
    }

    public void OnInit(IListener listener)
    {
        _listener = listener;
    }
}
