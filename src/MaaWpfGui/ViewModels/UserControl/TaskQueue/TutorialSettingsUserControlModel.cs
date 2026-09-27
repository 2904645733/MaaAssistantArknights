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
            if (baseTask is not TutorialTask tutorial)
            {
                return (null, []);
            }

            // 核心侧用「自定任务」通道执行我们的任务链：Custom 类型 + task_names
            var task = new AsstCustomTask {
                CustomTasks = [$"Tutorial@{tutorial.Segment}@Begin"],
            };
            return taskId switch {
                int id when id > 0 => (Instances.AsstProxy.AsstSetTaskParamsEncoded(id, task), [id]),
                null => FromSingle(Instances.AsstProxy.AsstAppendTaskWithEncoding(TaskType.Tutorial, task)),
                _ => (null, []),
            };
        }
    }
}
