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

            // 新手教程 = 【一个】核心任务。打哪一关由规则表里的句子决定，不再靠队列顺序。
            //
            //   Custom(Tutorial@Main@Begin)
            //     教程推进：认对话 → 做这句对应的动作 → 继续认下一句。
            //
            //     入口固定用 Tutorial@Main@Begin —— 它是包含全部段落的总入口
            //     （内部会回落到序章那段）。不能读 config 里的 Segment：
            //     老存档里存的可能是 "Prologue"，会把整段主界面流程漏掉。
            //
            //     需要打关卡时，规则表里那句话挂的是 J:<作业名> 动作，由插件自己起一个
            //     CopilotTask 打完（见 TutorialProgressTaskPlugin::run_copilot_job）：
            //         「选择行动地点。」        -> R:582,324,118,35 J:main_00-01.json
            //         「点击按钮开始演习项目。」 -> J:TR-1.json
            //
            // ★ 为什么不再排 5 个任务（旧版：Custom/Copilot/Custom/Copilot/Custom）：
            //   "什么时候打哪一关"由【队列位置】决定时，只要有一轮是从中间接着跑的，
            //   位置和游戏进度就会错开 —— 实测某轮只命中 TR-1 段的 3 条规则（前面 0-1
            //   早已跑过），① 跑完 TR-1 段结束，队列推进到 ② —— 而 ② 装的是 0-1 的作业，
            //   于是把 0-1 的三个部署点用在了 TR-1 的关卡详情页上（用户实际遇到的故障）。
            //   改成句子驱动后，从哪开始跑都对得上。
            //
            // ★ 作业为什么用【单作业】模式（FileName）而不是多作业（MultiTasks）：
            //   多作业模式会启用 MultiCopilotTaskPlugin 做关卡导航，而它的导航要 OCR
            //   核对关卡名（ClickedCorrectStage，roi [845,72,220,50]）——教程这一屏的
            //   关卡详情页上有关卡自己的对话文案挡着，关卡名核对必然失败，导航直接放弃、
            //   整条 Copilot 被停。
            //   实机日志：confirm_stage_name confirm stage name failed after retrying 3 times。
            //   单作业模式会把 MultiCopilotTaskPlugin 禁用（CopilotTask.cpp:90），
            //   完全不做关卡导航，直接从 BattleStartPre 开始 —— 关卡由规则表那一步自己点开。
            var ids = new List<int>();

            var task = new AsstCustomTask { CustomTasks = ["Tutorial@Main@Begin"] };
            var (ok, id) = Instances.AsstProxy.AsstAppendTaskWithEncoding(TaskType.Tutorial, task);
            if (!ok || id <= 0)
            {
                return (false, ids);
            }

            ids.Add(id);
            return (true, ids);
        }
    }
}
