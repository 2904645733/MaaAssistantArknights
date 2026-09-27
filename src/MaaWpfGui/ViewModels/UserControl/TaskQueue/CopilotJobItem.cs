// <copyright file="CopilotJobItem.cs" company="MaaAssistantArknights">
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

using MaaWpfGui.Configuration.Single.MaaTask;
using Stylet;

namespace MaaWpfGui.ViewModels.UserControl.TaskQueue;

/// <summary>
/// 高级设置中的作业项（勾选 + 拖动排序）。
/// </summary>
public class CopilotJobItem : PropertyChangedBase
{
    public CopilotJobItem(CopilotSnapshotJob model)
    {
        Model = model;
    }

    public CopilotSnapshotJob Model { get; }

    public bool IsChecked
    {
        get => Model.IsChecked;
        set {
            if (Model.IsChecked == value)
            {
                return;
            }

            Model.IsChecked = value;
            NotifyOfPropertyChange();
        }
    }

    public bool IsRaid => Model.IsRaid;

    public string Title => string.IsNullOrEmpty(Model.StageName)
        ? System.IO.Path.GetFileName(Model.FilePath)
        : Model.StageName!;
}
