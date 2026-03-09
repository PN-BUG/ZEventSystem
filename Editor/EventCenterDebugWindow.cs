#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace ZEventSystem.Editor
{
    public class EventCenterDebugWindow : EditorWindow
    {
        private string _search = "";
        private Vector2 _scroll;

        private string _manualEventName = "";
        private string _manualListenerName = "编辑器调试监听_1";

        private readonly Dictionary<string, List<EventCenter.DebugReg>> _grouped = new();
        private readonly Dictionary<string, bool> _foldouts = new();

        // eventName -> unique listener count
        private readonly Dictionary<string, int> _uniqueListenerCount = new();

        // temp set to count unique listeners per event (reuse to avoid GC)
        private readonly HashSet<string> _tmpListenerSet = new();

        private bool _needRefresh;

        // ======= UI Styles (cache) =======
        private bool _uiInited;
        private GUIStyle _card;
        private GUIStyle _cardHeader;
        private GUIStyle _mono;
        private GUIStyle _chip;
        private GUIStyle _chipWarn;
        private GUIStyle _chipErr;
        private GUIStyle _chipOk;
        private GUIStyle _tableHeader;
        private GUIStyle _row;
        private GUIStyle _rowAlt;
        private GUIStyle _miniRight;

        private GUIContent _icCopy;
        private GUIContent _icPlay;
        private GUIContent _icTrash;
        private GUIContent _icRefresh;
        private GUIContent _icInfo;

        private GUIStyle _badge;

        private Color _cBgRow = new Color(1f, 1f, 1f, 0.04f);
        private Color _cBgRowAlt = new Color(1f, 1f, 1f, 0.02f);
        private void InitStylesIfNeeded()
        {
            if (_uiInited) return;
            _uiInited = true;

            _badge = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                padding = new RectOffset(6, 6, 2, 2),
                fontSize = 10,
                normal =
                {
                    textColor = new Color(0.9f,0.9f,0.9f)
                }
            };

            _card = new GUIStyle(EditorStyles.helpBox)
            {
                padding = new RectOffset(10, 10, 8, 8),
                margin = new RectOffset(0, 0, 6, 6)
            };

            _cardHeader = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 12,
                padding = new RectOffset(2, 2, 2, 2)
            };

            _mono = new GUIStyle(EditorStyles.label)
            {
                font = Font.CreateDynamicFontFromOSFont(new[] { "Consolas", "Menlo", "Courier New" }, 11),
                richText = true
            };

            _chip = new GUIStyle(EditorStyles.miniButton)
            {
                fixedHeight = 18,
                padding = new RectOffset(8, 8, 0, 0),
                alignment = TextAnchor.MiddleCenter
            };

            _chipOk = new GUIStyle(_chip);
            _chipWarn = new GUIStyle(_chip);
            _chipErr = new GUIStyle(_chip);

            // 表头
            _tableHeader = new GUIStyle(EditorStyles.toolbar)
            {
                fixedHeight = 22,
                padding = new RectOffset(8, 8, 3, 3)
            };

            _row = new GUIStyle("CN EntryBackOdd") // Unity 内置列表条背景（不同版本可能存在差异）
            {
                padding = new RectOffset(8, 8, 4, 4)
            };
            _rowAlt = new GUIStyle("CN EntryBackEven")
            {
                padding = new RectOffset(8, 8, 4, 4)
            };

            _miniRight = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleRight
            };

            // icons
            _icCopy = EditorGUIUtility.IconContent("TreeEditor.Duplicate", "复制");
            if (_icCopy.image == null) _icCopy = EditorGUIUtility.IconContent("d_TreeEditor.Duplicate", "复制");

            _icPlay = EditorGUIUtility.IconContent("PlayButton", "触发(仅无参)");
            if (_icPlay.image == null) _icPlay = EditorGUIUtility.IconContent("d_PlayButton", "触发(仅无参)");

            _icTrash = EditorGUIUtility.IconContent("TreeEditor.Trash", "移除");
            if (_icTrash.image == null) _icTrash = EditorGUIUtility.IconContent("d_TreeEditor.Trash", "移除");

            _icRefresh = EditorGUIUtility.IconContent("Refresh", "刷新");
            if (_icRefresh.image == null) _icRefresh = EditorGUIUtility.IconContent("d_Refresh", "刷新");

            _icInfo = EditorGUIUtility.IconContent("console.infoicon", "信息");
            if (_icInfo.image == null) _icInfo = EditorGUIUtility.IconContent("d_console.infoicon", "信息");
        }

        [MenuItem("Tools/ZEventSystem/事件中心调试窗口")]
        public static void Open()
        {
            var w = GetWindow<EventCenterDebugWindow>("事件中心调试");
            w.minSize = new Vector2(900, 520);
            w.Refresh();
        }

        private void OnEnable() => Refresh();

        private void Refresh()
        {
            var regs = EventCenter.Debug_GetAllRegs();

            _grouped.Clear();
            _uniqueListenerCount.Clear();

            // 先分组
            for (int i = 0; i < regs.Count; i++)
            {
                var r = regs[i];
                if (!_grouped.TryGetValue(r.EventName, out var list))
                {
                    list = new List<EventCenter.DebugReg>(8);
                    _grouped.Add(r.EventName, list);

                    if (!_foldouts.ContainsKey(r.EventName))
                        _foldouts[r.EventName] = false;
                }
                list.Add(r);
            }

            // 再统计每个事件的 unique listener 数
            foreach (var kv in _grouped)
            {
                _tmpListenerSet.Clear();
                var list = kv.Value;
                for (int i = 0; i < list.Count; i++)
                    _tmpListenerSet.Add(list[i].ListenerName);

                _uniqueListenerCount[kv.Key] = _tmpListenerSet.Count;
            }
            _tmpListenerSet.Clear();

            Repaint();
        }

        private void OnGUI()
        {
       
            DrawToolbar();
            EditorGUILayout.Space(8);
            DrawManualArea();
            EditorGUILayout.Space(8);
            DrawEventList();

            // ✅ 统一在 OnGUI 最后刷新，避免在绘制过程中修改集合导致布局异常
            if (_needRefresh)
            {
                _needRefresh = false;
                Refresh();
            }
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                _search = GUILayout.TextField(_search, EditorStyles.toolbarSearchField, GUILayout.MinWidth(240));

                // 清空搜索（右侧 X）
                if (GUILayout.Button("X", EditorStyles.toolbarButton, GUILayout.Width(32)))//toolbarSearchFieldCancelButton
                {
                    _search = "";
                    GUI.FocusControl(null);
                }

                GUILayout.Space(8);

                if (GUILayout.Button("刷新", EditorStyles.toolbarButton, GUILayout.Width(60)))
                    _needRefresh = true;

                GUILayout.FlexibleSpace();

                if (GUILayout.Button("清空全部", EditorStyles.toolbarButton, GUILayout.Width(80)))
                {
                    if (EditorUtility.DisplayDialog("清空事件中心", "确定清空所有事件注册吗？", "确定", "取消"))
                    {
                        EventCenter.Clear();
                        _needRefresh = true;
                    }
                }
            }
        }

        private void DrawManualArea()
        {
            EditorGUILayout.LabelField("手动调试", EditorStyles.boldLabel);

            using (new EditorGUILayout.VerticalScope(GUI.skin.box))
            {
                _manualEventName = EditorGUILayout.TextField("事件名（string）", _manualEventName);
                _manualListenerName = EditorGUILayout.TextField("监听者名字", _manualListenerName);

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("添加调试监听（无参：触发时打印日志）", GUILayout.Height(24)))
                    {
                        if (string.IsNullOrEmpty(_manualEventName))
                        {
                            EditorUtility.DisplayDialog("提示", "事件名不能为空。", "知道了");
                        }
                        else
                        {
                            EventCenter.Debug_AddLogListener(_manualEventName, _manualListenerName, "[事件中心调试]");
                            _needRefresh = true;
                        }
                    }

                    if (GUILayout.Button("移除此事件（全部注册）", GUILayout.Height(24), GUILayout.Width(170)))
                    {
                        if (!string.IsNullOrEmpty(_manualEventName))
                        {
                            var n = EventCenter.Debug_RemoveEvent(_manualEventName);
                            Debug.Log($"[事件中心调试] 已移除事件 '{_manualEventName}' 的注册数量：{n}");
                            _needRefresh = true;
                        }
                    }

                    if (GUILayout.Button("复制事件名", GUILayout.Height(24), GUILayout.Width(100)))
                    {
                        if (!string.IsNullOrEmpty(_manualEventName))
                        {
                            EditorGUIUtility.systemCopyBuffer = _manualEventName;
                            ShowNotification(new GUIContent("已复制事件名"));
                        }
                    }
                }
            }
        }
        private void DrawEventList()
        {
            InitStylesIfNeeded();

            // ===== Summary Bar =====
            int totalRegs = 0;
            foreach (var kv in _grouped) totalRegs += kv.Value.Count;

            using (new EditorGUILayout.HorizontalScope(_tableHeader))
            {
                GUILayout.Label($"事件列表  <b>{_grouped.Count}</b>    注册总数  <b>{totalRegs}</b>", _mono);
                GUILayout.FlexibleSpace();

                if (IconBtn(_icRefresh, 24))
                    _needRefresh = true;
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            int cardIndex = 0;
            foreach (var kv in _grouped)
            {
                var eventName = kv.Key;
                if (!PassSearch(eventName)) continue;

                var list = kv.Value;

                // foldout
                if (!_foldouts.TryGetValue(eventName, out var open))
                    open = false;

                // unique listeners
                _uniqueListenerCount.TryGetValue(eventName, out var uniqCount);

                // trigger stat
                int attempt, success, fail, failReason;
                double lastFailTime;
                string lastEx;
                EventCenter.Debug_TryGetTriggerStat(eventName, out attempt, out success, out fail, out lastFailTime, out failReason, out lastEx);

                // TR count
                int trCount = 0;
                for (int i = 0; i < list.Count; i++)
                    if (list[i].IsTR) trCount++;

                bool hasDupListener = uniqCount < list.Count;
                bool hasTRConflict = trCount > 1;
                bool hasFail = fail > 0;

                // ===== Card =====
                using (new EditorGUILayout.VerticalScope(_card))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        // 左侧：Foldout + 名称
                        _foldouts[eventName] = EditorGUILayout.Foldout(open, eventName, true);
                        //GUILayout.Label(eventName, _cardHeader, GUILayout.MinWidth(80), GUILayout.MaxWidth(260));

                        // 把右侧区域顶到最右边
                        GUILayout.FlexibleSpace();
                        // 中间：统计
                        GUILayout.Label(
                            $"Attempt {attempt}  •  Success {success}  •  Fail {fail}",
                            EditorStyles.miniLabel,
                            GUILayout.MinWidth(180),
                            GUILayout.MaxWidth(260)
                        );

                        if (hasFail)
                        {
                            string reasonText = failReason switch
                            {
                                1 => "NotFound",
                                2 => "NullDelegate",
                                3 => "Exception",
                                _ => "Unknown"
                            };

                            GUILayout.Label(
                                $"LastFail {FormatTime(lastFailTime)} ({reasonText})",
                                _miniRight,
                                GUILayout.MinWidth(120),
                                GUILayout.MaxWidth(220)
                            );
                        }

                 

                        // 右侧：状态 Chips
                        DrawChip($"Regs {list.Count}");
                        DrawChip($"Listeners {uniqCount}", hasDupListener ? _chipWarn : _chipOk);

                        if (hasFail) DrawChip($"Fail {fail}", _chipWarn);
                        if (hasTRConflict) DrawChip($"TR {trCount}", _chipErr);

                        GUILayout.Space(8);

                        // 右侧操作按钮
                        if (IconBtn(_icCopy, 26))
                        {
                            EditorGUIUtility.systemCopyBuffer = eventName;
                            _manualEventName = eventName;
                            ShowNotification(new GUIContent("已复制事件名"));
                        }

                        if (IconBtn(_icPlay, 26))
                        {
                            EventCenter.EventTrigger(eventName);
                            _needRefresh = true;
                        }

                        var oldBg = GUI.backgroundColor;
                        GUI.backgroundColor = new Color(0.65f, 0.2f, 0.2f);
                        if (IconBtn(_icTrash, 26))
                        {
                            var n = EventCenter.Debug_RemoveEvent(eventName);
                            Debug.Log($"[事件中心调试] 已移除事件 '{eventName}' 的注册数量：{n}");
                            _needRefresh = true;
                            GUI.backgroundColor = oldBg;
                            break;
                        }
                        GUI.backgroundColor = oldBg;
                    }

                    // ---- Context warnings (compact) ----
                    if (hasDupListener)
                    {
                        EditorGUILayout.HelpBox(
                            $"ListenerName 冲突：注册 {list.Count}，唯一监听 {uniqCount}。建议实现 IOnlyOneID 或 ListenerName 加 InstanceID。",
                            MessageType.Warning);
                    }

                    if (hasTRConflict)
                    {
                        EditorGUILayout.HelpBox(
                            $"TR 事件只能有一个监听者，目前 {trCount} 个（同事件同签名被多对象注册）。",
                            MessageType.Error);
                    }

                    if (hasFail && !string.IsNullOrEmpty(lastEx))
                    {
                        // 失败异常用折叠式短文本，避免撑爆界面
                        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                        {
                            GUILayout.Label("Last Exception", EditorStyles.miniBoldLabel);
                            EditorGUILayout.SelectableLabel(lastEx, EditorStyles.wordWrappedMiniLabel, GUILayout.MinHeight(18));
                        }
                    }

                    if (eventName == "eventName")
                    {
                        EditorGUILayout.HelpBox(
                            "事件名疑似写错：AddListener(\"eventName\", ...) 传了字符串字面量，应传变量 AddListener(eventName, ...)。",
                            MessageType.Warning);
                    }

                    // ---- Expanded details ----
                    if (_foldouts[eventName])
                    {
                        // 表头
                        using (new EditorGUILayout.HorizontalScope(_tableHeader))
                        {
                            GUILayout.Label("监听者", EditorStyles.miniBoldLabel, GUILayout.Width(280));
                            GUILayout.Label("类型", EditorStyles.miniBoldLabel, GUILayout.Width(70));
                            GUILayout.Label("委托签名", EditorStyles.miniBoldLabel, GUILayout.Width(240));
                            GUILayout.FlexibleSpace();
                            GUILayout.Label("操作", EditorStyles.miniBoldLabel, GUILayout.Width(330));
                        }

                        // 行
                        for (int i = 0; i < list.Count; i++)
                        {
                            var r = list[i];
                            var bg = (i % 2 == 0) ? _row : _rowAlt;

                            using (new EditorGUILayout.HorizontalScope(bg))
                            {
                                GUILayout.Label(r.ListenerName, GUILayout.Width(280));
                                GUILayout.Label(r.IsTR ? "TR" : "Void", GUILayout.Width(70));
                                GUILayout.Label(r.DelegateTypeName, GUILayout.Width(240));

                                GUILayout.FlexibleSpace();

                                if (GUILayout.Button("复制监听者名称", GUILayout.Width(96)))
                                {
                                    EditorGUIUtility.systemCopyBuffer = r.ListenerName;
                                    _manualListenerName = r.ListenerName;
                                    ShowNotification(new GUIContent("已复制监听者名"));
                                }

                                if (GUILayout.Button("复制事件名称", GUILayout.Width(80)))
                                {
                                    EditorGUIUtility.systemCopyBuffer = r.EventName;
                                    _manualEventName = r.EventName;
                                    ShowNotification(new GUIContent("已复制事件名"));
                                }

                                if (GUILayout.Button("移除", GUILayout.Width(80)))
                                {
                                    EventCenter.Debug_RemoveOneReg(r.EventName, r.ListenerName, r.DelegateType, r.IsTR);
                                    _needRefresh = true;
                                    break;
                                }

                                if (GUILayout.Button("移除此监听者(此事件)", GUILayout.Width(160)))
                                {
                                    var n = EventCenter.Debug_RemoveListenerFromEvent(r.EventName, r.ListenerName);
                                    Debug.Log($"[事件中心调试] 已移除监听者 '{r.ListenerName}' 在事件 '{r.EventName}' 下的注册数量：{n}");
                                    _needRefresh = true;
                                    break;
                                }
                            }
                        }
                    }
                }

                cardIndex++;
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawChip(string text, GUIStyle style = null)
        {
            style ??= _chip;
            var old = GUI.backgroundColor;

            // 给不同 style 轻微底色（不写死颜色也行，但业内通常会有轻微状态色）
            if (style == _chipOk) GUI.backgroundColor = new Color(0.25f, 0.55f, 0.35f, 0.35f);
            else if (style == _chipWarn) GUI.backgroundColor = new Color(0.75f, 0.55f, 0.15f, 0.35f);
            else if (style == _chipErr) GUI.backgroundColor = new Color(0.75f, 0.2f, 0.2f, 0.35f);
            else GUI.backgroundColor = new Color(1f, 1f, 1f, 0.12f);

            GUILayout.Label(text, style);

            GUI.backgroundColor = old;
        }

        private bool IconBtn(GUIContent icon, float width)
        {
            // 用 toolbarButton 质感更像 Unity 官方窗口
            return GUILayout.Button(icon, EditorStyles.toolbarButton, GUILayout.Width(width), GUILayout.Height(20));
        }
        //private void DrawEventList()
        //{
        //    // ===== 头部统计 =====
        //    EditorGUILayout.LabelField($"事件列表（{_grouped.Count}）", EditorStyles.boldLabel);

        //    int totalRegs = 0;
        //    foreach (var kv in _grouped) totalRegs += kv.Value.Count;

        //    EditorGUILayout.LabelField(
        //        $"事件数：{_grouped.Count}    注册总数：{totalRegs}",
        //        EditorStyles.miniBoldLabel);

        //    // ===== 滚动区域 =====
        //    _scroll = EditorGUILayout.BeginScrollView(_scroll);

        //    foreach (var kv in _grouped)
        //    {
        //        var eventName = kv.Key;
        //        if (!PassSearch(eventName)) continue;

        //        var list = kv.Value;

        //        // foldout 状态
        //        if (!_foldouts.TryGetValue(eventName, out var open))
        //            open = false;

        //        // unique listener count
        //        _uniqueListenerCount.TryGetValue(eventName, out var uniqCount);

        //        // ===== 触发统计（含失败）=====
        //        int attempt, success, fail, failReason;
        //        double lastFailTime;
        //        string lastEx;
        //        EventCenter.Debug_TryGetTriggerStat(
        //            eventName,
        //            out attempt, out success, out fail,
        //            out lastFailTime, out failReason, out lastEx);

        //        // TR count
        //        int trCount = 0;
        //        for (int i = 0; i < list.Count; i++)
        //            if (list[i].IsTR) trCount++;

        //        // ===== 卡片：根据状态着色（轻量，不改整体皮肤）=====
        //        var oldColor = GUI.color;
        //        if (trCount > 1) GUI.color = new Color(1f, 0.75f, 0.75f);            // TR冲突偏红
        //        else if (fail > 0) GUI.color = new Color(1f, 0.9f, 0.75f);            // 有失败偏橙
        //        else if (uniqCount < list.Count) GUI.color = new Color(1f, 1f, 0.78f);// 监听者冲突偏黄
        //        else GUI.color = Color.white;

        //        using (new EditorGUILayout.VerticalScope(GUI.skin.box))
        //        {
        //            GUI.color = oldColor;

        //            // ===== 标题行 =====
        //            using (new EditorGUILayout.HorizontalScope())
        //            {
        //                _foldouts[eventName] = EditorGUILayout.Foldout(
        //                    open,
        //                    $"{eventName}",
        //                    true);

        //                GUILayout.FlexibleSpace();

        //                // 右侧小统计（更紧凑）
        //                GUILayout.Label($"注册 {list.Count}", EditorStyles.miniBoldLabel, GUILayout.Width(62));
        //                GUILayout.Label($"监听 {uniqCount}", EditorStyles.miniBoldLabel, GUILayout.Width(62));

        //                if (fail > 0)
        //                    GUILayout.Label($"失败 {fail}", EditorStyles.miniBoldLabel, GUILayout.Width(62));

        //                if (trCount > 1)
        //                    GUILayout.Label($"TR {trCount}", EditorStyles.miniBoldLabel, GUILayout.Width(52));

        //                GUILayout.Space(8);

        //                if (GUILayout.Button("复制", GUILayout.Width(60)))
        //                {
        //                    EditorGUIUtility.systemCopyBuffer = eventName;
        //                    ShowNotification(new GUIContent("已复制事件名"));
        //                }

        //                // 只无参触发（避免误导）
        //                if (GUILayout.Button("触发（仅无参）", GUILayout.Width(110)))
        //                {
        //                    EventCenter.EventTrigger(eventName);
        //                    _needRefresh = true; // 刷新统计（成功/失败/异常）
        //                }

        //                // 红色移除按钮
        //                var oldBg = GUI.backgroundColor;
        //                GUI.backgroundColor = new Color(1f, 0.35f, 0.35f, 1f);
        //                if (GUILayout.Button("移除事件", GUILayout.Width(80)))
        //                {
        //                    var n = EventCenter.Debug_RemoveEvent(eventName);
        //                    Debug.Log($"[事件中心调试] 已移除事件 '{eventName}' 的注册数量：{n}");
        //                    _needRefresh = true;
        //                    GUI.backgroundColor = oldBg;
        //                    break; // 集合将变化，退出本次循环，等 Refresh 后再画
        //                }
        //                GUI.backgroundColor = oldBg;
        //            }

        //            // ===== 统计行（Attempt/Success/Fail + 最后失败原因）=====
        //            using (new EditorGUILayout.HorizontalScope())
        //            {
        //                GUILayout.Label($"尝试：{attempt}   成功：{success}   失败：{fail}", EditorStyles.miniLabel);

        //                if (fail > 0)
        //                {
        //                    GUILayout.Space(10);

        //                    string reasonText = failReason switch
        //                    {
        //                        1 => "未找到事件/表",
        //                        2 => "委托为空",
        //                        3 => "执行抛异常",
        //                        _ => "未知"
        //                    };

        //                    GUILayout.Label($"最后失败：{FormatTime(lastFailTime)}   原因：{reasonText}", EditorStyles.miniLabel);

        //                    if (!string.IsNullOrEmpty(lastEx))
        //                        GUILayout.Label($"  异常：{lastEx}", EditorStyles.miniLabel);
        //                }
        //            }

        //            // ===== 提示块 =====
        //            if (uniqCount < list.Count)
        //            {
        //                EditorGUILayout.HelpBox(
        //                    $"⚠ ListenerName 冲突：注册数 {list.Count}，唯一监听者 {uniqCount}\n" +
        //                    $"建议：让监听对象实现 IOnlyOneID（或在 ListenerName 中拼 InstanceID）。",
        //                    MessageType.Warning);
        //            }

        //            if (trCount > 1)
        //            {
        //                EditorGUILayout.HelpBox(
        //                    $"❌ TR 事件同事件同签名只能有一个监听者，目前有 {trCount} 个。",
        //                    MessageType.Error);
        //            }

        //            if (eventName == "eventName")
        //            {
        //                EditorGUILayout.HelpBox(
        //                    "⚠ 事件名疑似写错：你可能写成 AddListener(\"eventName\", ...)（字符串字面量），应该传变量 AddListener(eventName, ...)。",
        //                    MessageType.Warning);
        //            }

        //            // ===== 明细（展开后）=====
        //            if (_foldouts[eventName])
        //            {
        //                EditorGUILayout.Space(4);
        //                DrawHeaderRow();

        //                for (int i = 0; i < list.Count; i++)
        //                {
        //                    var r = list[i];

        //                    // 斑马纹背景
        //                    var rowBg = (i & 1) == 1 ? new Color(0, 0, 0, 0.08f) : new Color(0, 0, 0, 0.02f);
        //                    var oldRowBg = GUI.backgroundColor;
        //                    GUI.backgroundColor = rowBg;

        //                    using (new EditorGUILayout.VerticalScope(GUI.skin.box))
        //                    {
        //                        GUI.backgroundColor = oldRowBg;

        //                        using (new EditorGUILayout.HorizontalScope())
        //                        {
        //                            GUILayout.Label(r.ListenerName, GUILayout.Width(280));
        //                            GUILayout.Label(r.IsTR ? "TR" : "Void", GUILayout.Width(70));
        //                            GUILayout.Label(r.DelegateTypeName, GUILayout.Width(240));

        //                            GUILayout.FlexibleSpace();

        //                            if (GUILayout.Button("复制监听者", GUILayout.Width(90)))
        //                            {
        //                                EditorGUIUtility.systemCopyBuffer = r.ListenerName;
        //                                _manualListenerName = r.ListenerName;
        //                                ShowNotification(new GUIContent("已复制监听者名"));
        //                            }

        //                            if (GUILayout.Button("复制事件", GUILayout.Width(70)))
        //                            {
        //                                EditorGUIUtility.systemCopyBuffer = r.EventName;
        //                                _manualEventName = r.EventName;
        //                                ShowNotification(new GUIContent("已复制事件名"));
        //                            }

        //                            if (GUILayout.Button("移除这条", GUILayout.Width(80)))
        //                            {
        //                                EventCenter.Debug_RemoveOneReg(r.EventName, r.ListenerName, r.DelegateType, r.IsTR);
        //                                _needRefresh = true;
        //                                break; // 等 Refresh 后再画
        //                            }

        //                            if (GUILayout.Button("移除此监听者(此事件)", GUILayout.Width(170)))
        //                            {
        //                                var n = EventCenter.Debug_RemoveListenerFromEvent(r.EventName, r.ListenerName);
        //                                Debug.Log($"[事件中心调试] 已移除监听者 '{r.ListenerName}' 在事件 '{r.EventName}' 下的注册数量：{n}");
        //                                _needRefresh = true;
        //                                break; // 等 Refresh 后再画
        //                            }
        //                        }
        //                    }
        //                }
        //            }
        //        }
        //    }

        //    EditorGUILayout.EndScrollView();
        //}
        string FormatTime(double t)
        {
            if (t <= 0) return "从未";

            double diff = EditorApplication.timeSinceStartup - t;

            if (diff < 1) return "刚刚";
            if (diff < 60) return $"{diff:F1}s前";
            if (diff < 3600) return $"{diff / 60:F1}m前";

            return $"{diff / 3600:F1}h前";
        }
        private void DrawHeaderRow()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("监听者", EditorStyles.miniBoldLabel, GUILayout.Width(280));
                GUILayout.Label("类型", EditorStyles.miniBoldLabel, GUILayout.Width(70));
                GUILayout.Label("委托签名", EditorStyles.miniBoldLabel, GUILayout.Width(240));
                GUILayout.FlexibleSpace();
                GUILayout.Label("操作", EditorStyles.miniBoldLabel, GUILayout.Width(560));
            }
        }

        private bool PassSearch(string eventName)
        {
            if (string.IsNullOrEmpty(_search)) return true;
            return eventName.IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
#endif