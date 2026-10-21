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
    /// Gets 教程段落清单：面板上一个方框一段，按游戏里的先后顺序排。
    /// <para>
    /// ★ 这张表和核心侧的规则表是【同一个东西的两面】，两边必须一致：
    /// 核心侧真源 = 新手教程\进度表.json → 规则表.txt → resource/tasks/Tutorial/Progress.json；
    /// 本列表 = 同一批分段名，只用于显示。
    /// 核对脚本：新手教程\工具\核对面板分段列表.py（检查两边段名与顺序是否一致）。
    /// </para>
    /// <para>
    /// 起名点确定之后那段开场剧情属于「序章·上」的一部分（流程里会先自动跳过），不单独占一项。
    /// 名字照游戏里的显示写：还没公开关卡代号时游戏显示成「??? 序章·上」。
    /// </para>
    /// <para>
    /// 「0-3 / TR-3 / 0-4 / 0-5 / 0-6 / TR-5」这几段在规则表里只有占位句、没有自己的对话：
    /// 打关走终端链（核心认进度卡 → OCR 读关卡名 → 找 resource/copilot/&lt;关卡名&gt;.json），
    /// 0-4 / 0-5 弹出的「点击按钮进入快速编队页面」复用 0-2 那条规则。
    /// </para>
    /// </summary>
    public IReadOnlyList<string> ImplementedStages { get; } = [
        "??? 序章·上",
        "??? 序章·下",
        "主界面",
        "任务",
        "TR-1",
        "0-2",
        "公招",
        "TR-2",
        "0-3",
        "TR-3",
        "0-4",
        "干员升级",
        "0-5",
        "0-6",
        "技能升级",
        "TR-5",
        "0-7",

        // 0-7 之后（顺序按用户给的）
        "TR-6",
        "0-8",
        "0-9",
        "TR-7",
        "0-10",
        "资源收集",
        "0-11",
        "基建",
        "1-1",
        "控制中心升级",
        "1-2",
        "干员培养",
        "1-3",
        "1-4",
        "1-5",
        "剿灭",
        "1-6",
        "TR-8",
        "TR-9",
        "1-7",
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
