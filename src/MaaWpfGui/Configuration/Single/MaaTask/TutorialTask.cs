// <copyright file="TutorialTask.cs" company="MaaAssistantArknights">
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
using static MaaWpfGui.Main.AsstProxy;

namespace MaaWpfGui.Configuration.Single.MaaTask;

/// <summary>
/// 新手教程：认对话推进，遇到教学要求的拖拽 / 点击就照做。
/// 教学关进关由游戏自己完成，既没有「开始行动」也没有结算，所以整段由核心的 Tutorial@ 任务链驱动
/// （见 resource/tasks/Tutorial/*.json），这里只负责把段名交给核心。
/// </summary>
public class TutorialTask : BaseTask
{
    public TutorialTask() => TaskType = TaskType.Tutorial;

    /// <summary>
    /// Gets or sets 要跑的教程段，对应资源里的 Tutorial@{段}@Begin。
    /// Main = 从序章（上/下）一路到主界面的抽卡 / 编队 / 回行动现场选 0-1（内部会先跑序章那段）。
    /// </summary>
    public string Segment { get; set; } = "Main";
}
