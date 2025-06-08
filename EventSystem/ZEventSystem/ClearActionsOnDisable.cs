using UnityEngine;
using ZEventSystem;

public class ClearActionsOnDisable : MonoBehaviour
{
    string key;
    private void OnDisable()
    {
        EventCenter.ClearGameObjectActions(key);
    }
    public void OnInit(string key)
    {
        this.key = key;
    }
}
