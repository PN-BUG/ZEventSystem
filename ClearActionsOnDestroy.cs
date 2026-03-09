
using UnityEngine;
using ZEventSystem;

public class ClearActionsOnDestroy : MonoBehaviour
{
    private IListener _listener;
    private void OnDestroy()
    {
        // 清掉该 listener 注册过的所有事件（O(注册数)）
        _listener?.RemoveALL_ThisObjListenter();
    }
    public void OnInit(IListener listener)
    {
        _listener = listener;
    }
}
