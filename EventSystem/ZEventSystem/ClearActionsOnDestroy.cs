
using UnityEngine;
using ZEventSystem;

public class ClearActionsOnDestroy : MonoBehaviour
{
    string key;
    private void OnDestroy()
    {
        EventCenter.ClearGameObjectActions(key);
    }
    public void OnInit(string key)
    {
        this.key = key;
    }
}
