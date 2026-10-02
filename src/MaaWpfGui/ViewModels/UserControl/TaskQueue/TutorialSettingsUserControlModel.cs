// <copyright file="TutorialSettingsUserControlModel.cs" company="MaaAssistantArknights">
// Part of the MaaWpfGui project, maintained by the MaaAssistantArknights team (Maa Team)
// Copyright (C) 2021-2025 MaaAssistantArknights Contributors
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU Affero General Public License v3.0 only as published by
// the Free Software Foundation, either version 3 of the License, or
// any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY
// </copyright>

#nullable enable
using System.Collections.Generic;
using System.IO;
using MaaWpfGui.Configuration.Single.MaaTask;
using MaaWpfGui.Helper;
using MaaWpfGui.Models.AsstTasks;
using static MaaWpfGui.Main.AsstProxy;

namespace MaaWpfGui.ViewModels.UserControl.TaskQueue;

/// <summary>
/// 新手教程任务的设置面板模型。
/// 面板里没有可调项：教程段目前只有序章，进度由核心按对话内容自己认。
/// </summary>
public class TutorialSettingsUserControlModel : TaskSettingsViewModel, TutorialSettingsUserControlModel.ISerialize
{
    static TutorialSettingsUserControlModel()
    {
        Instance = new();
    }

    /// <summary>
    /// Gets 单例。
    /// </summary>
    public static TutorialSettingsUserControlModel Instance { get; }

    /// <summary>
    /// Gets 已经写好、MAA 现在就能带着跑的教学段落（面板上用方框一个一个列出来，按游戏里的先后顺序）。
    /// 起名点确定之后那段开场剧情属于 ??? 序章·上 的一部分（流程里会先自动跳过），不单独占一项。
    /// 名字照游戏里的显示写：还没公开关卡代号时游戏显示成「??? 序章·上」。
    /// 以后每做完一段，在这里和 resource/tasks/Tutorial 里各加一处。
    /// </summary>
    public IReadOnlyList<string> ImplementedStages { get; } = [
        "??? 序章·上",
        "??? 序章·下",
        "抽卡教程",
        "编队教程",
        "0-1",
        "任务",
        "TR-1",
    ];

    /// <inheritdoc/>
    public override void RefreshUI(BaseTask baseTask)
    {
        if (baseTask is TutorialTask)
        {
            Refresh();
        }
    }

    /// <inheritdoc/>
    public override (bool? IsSuccess, IEnumerable<int> TaskId) SerializeTask(BaseTask? baseTask, int? taskId = null) => (this as ISerialize).Serialize(baseTask, taskId);

    private interface ISerialize : ITaskQueueModelSerialize
    {
        (bool? IsSuccess, IEnumerable<int> TaskId) ITaskQueueModelSerialize.Serialize(BaseTask? baseTask, int? taskId)
        {
            if (baseTask is not TutorialTask)
            {
                return (null, []);
            }

            // 新手教程 = 【三个】核心任务依次入队，靠队列本身串起来（和「战斗任务」把
            // 小任务列表展开成一串任务完全同一个套路）：
            //
            //   ① Custom(Tutorial@Main@Begin)
            //      教程推进：认对话 → 点/拖 → 点 0-1 卡片 → 本段结束。
            //      入口固定用 Tutorial@Main@Begin —— 它是包含全部段落的总入口
            //      （内部会回落到序章那段）。不能读 config 里的 Segment：
            //      老存档里存的可能是 "Prologue"，会把整段主界面流程漏掉。
            //
            //   ② Copilot → resource/copilot/main_00-01.json
            //      自动战斗：从「开始行动」那一步开始 → 编队 → 战斗。
            //
            //      ★ 用【单作业】模式（FileName）而不是多作业（MultiTasks），这是刻意的：
            //        多作业模式会启用 MultiCopilotTaskPlugin 做关卡导航，而它的导航要 OCR
            //        核对关卡名（ClickedCorrectStage，roi [845,72,220,50]）——教程这一屏的
            //        关卡详情页上有关卡自己的对话文案挡着，关卡名核对必然失败，导航直接放弃、
            //        整条 Copilot 被停。
            //        实机日志：confirm_stage_name confirm stage name failed after retrying 3 times。
            //        单作业模式会把 MultiCopilotTaskPlugin 禁用（CopilotTask.cpp:90），
            //        完全不做关卡导航，直接从 BattleStartPre 开始 —— 关卡由教程那一步
            //        （规则表里的 R:582,324,118,35）自己点开。
            //
            //   ③ Custom(Tutorial@Main@Begin) —— 再入一次，继续后面的教学
            //      ① 那一段跑到「选择行动地点」就结束了（规则表里那条带 X 动作），后面的
            //      教学（任务段等）还没跑。而 ② 打完会把画面停在【关卡结算画面】上，
            //      那屏没有对话框、没有对话标志 —— 插件里装了"退结算"（第一次没看到对话
            //      就反复点右上角那个角落，和官方退出结算用的是同一个点 [1260,100,10,10]），
            //      点掉结算后就接着认对话往下走（任务段 → TR-1 段）。
            //
            //   ④ Copilot → resource/copilot/TR-1.json
            //      TR-1 也是"教学关"，同样用单作业模式（原因见 ② 的说明）。
            //
            //   ⑤ Custom(Tutorial@Main@Begin) —— 再入一次，退掉 TR-1 的结算并继续
            //      （和 ③ 同一个道理：打完了停在结算画面，得让插件先点掉）。
            //
            // 为什么不在资源表里加自定义动作（曾经写过 H:main_00-01，已废弃）：
            // 往官方动作命名空间里塞自己的前缀，万一上游自己加了同名动作就会撞车。
            // 展开成一串任务既不碰核心、也不碰资源表的语法。
            var ids = new List<int>();
            foreach (var step in new (bool IsCopilot, string File)[]
            {
                (false, "Tutorial@Main@Begin"),   // ① 教程推进（到「选择行动地点」收尾）
                (true, "main_00-01.json"),        // ② 自动战斗打 0-1
                (false, "Tutorial@Main@Begin"),   // ③ 退 0-1 结算 → 任务段 → TR-1 段
                (true, "TR-1.json"),              // ④ 自动战斗打 TR-1
                (false, "Tutorial@Main@Begin"),   // ⑤ 退 TR-1 结算 → 继续后面的教学
            })
            {
                AsstBaseTask task = step.IsCopilot
                    ? new AsstCopilotTask {
                        FileName = Path.Combine(PathsHelper.ResourceDir, "copilot", step.File),

                        // 教学关是游戏给定阵容，不让 MAA 去点「快捷编队」跟教程抢操作。
                        Formation = false,
                        LoopTimes = 1,
                    }
                    : new AsstCustomTask {
                        CustomTasks = [step.File],
                    };

                var (ok, id) = Instances.AsstProxy.AsstAppendTaskWithEncoding(TaskType.Tutorial, task);
                if (!ok || id <= 0)
                {
                    return (false, ids);
                }

                ids.Add(id);
            }

            return (true, ids);
        }
    }
}
