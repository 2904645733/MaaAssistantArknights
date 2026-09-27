// <copyright file="CopilotSubTaskItem.cs" company="MaaAssistantArknights">
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

using System;
using MaaWpfGui.Configuration.Single.MaaTask;
using MaaWpfGui.Helper;
using Stylet;

namespace MaaWpfGui.ViewModels.UserControl.TaskQueue;

/// <summary>
/// 战斗任务中的一个小任务的界面包装。
/// </summary>
public class CopilotSubTaskItem : PropertyChangedBase
{
    public CopilotSubTaskItem(CopilotSubTask model)
    {
        Model = model;
    }

    public CopilotSubTask Model { get; }

    private bool _isEditing;

    /// <summary>
    /// Gets or sets a value indicating whether 名字处于编辑状态（仅点重命名按钮后为真）。
    /// </summary>
    public bool IsEditing
    {
        get => _isEditing;
        set => SetAndNotify(ref _isEditing, value);
    }

    public string Name
    {
        get => Model.Name;
        set {
            Model.Name = value;
            NotifyOfPropertyChange();
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether 该小任务是否勾选执行（跑完会自动取消勾选，和作业列表一致）。
    /// </summary>
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

    /// <summary>
    /// Gets a value indicating whether 该小任务是战斗（作业页快照）。
    /// </summary>
    public bool IsBattle => Model.Kind == CopilotSubTaskKind.Battle;

    /// <summary>
    /// Gets a value indicating whether 该小任务是导航（章节入口切换，只有删除按钮）。
    /// </summary>
    public bool IsNav => Model.Kind == CopilotSubTaskKind.Nav;

    /// <summary>
    /// Gets a value indicating whether 该小任务是「剿灭导航」（图标显示对 / 错）。
    /// </summary>
    public bool IsAnnihilationNav => Model.Kind == CopilotSubTaskKind.Nav && Model.NavAnnihilation;

    /// <summary>
    /// Gets a value indicating whether 剿灭导航已经从"下一个作战任务的作业"里读到了剿灭关卡名。
    /// 读到 = 图标 ✓（这一步能执行），读不到 = 图标 ✗（下发时会跳过并写错误日志）。
    /// </summary>
    public bool HasAnnihilationStage => !string.IsNullOrEmpty(Model.NavAnnihilationStage);

    /// <summary>Gets a value indicating whether 显示战斗图标（斜向短剑）。</summary>
    public bool ShowBattleIcon => !IsNav;

    /// <summary>Gets a value indicating whether 显示普通导航图标（向右箭头）。</summary>
    public bool ShowNavIcon => IsNav && !IsAnnihilationNav;

    /// <summary>Gets a value indicating whether 显示剿灭导航"识别到了"的图标（✓）。</summary>
    public bool ShowAnnihilationOkIcon => IsAnnihilationNav && HasAnnihilationStage;

    /// <summary>Gets a value indicating whether 显示剿灭导航"没识别到"的图标（✗）。</summary>
    public bool ShowAnnihilationFailIcon => IsAnnihilationNav && !HasAnnihilationStage;

    /// <summary>
    /// Gets 剿灭导航图标的提示：图钉同样是拖拽把手，所以第一行固定写"标签顺序可拖动"，
    /// 第二行再写读到了就切到哪一关、读不到时去哪儿找关卡名。
    /// </summary>
    public string AnnihilationIconTip => IsAnnihilationNav
        ? LocalizationHelper.GetString("LabelSequenceTip") + Environment.NewLine
          + LocalizationHelper.GetStringFormat(
              HasAnnihilationStage ? "CopilotNavAnnihilationOkTip" : "CopilotNavAnnihilationFailTip",
              Model.NavAnnihilationStage ?? string.Empty)
        : string.Empty;

    /// <summary>
    /// 剿灭关卡名重新解析完之后刷新名字、图标和提示（图标可能从 ✗ 变成 ✓，也可能反过来）。
    /// </summary>
    public void RefreshAnnihilationStage()
    {
        NotifyOfPropertyChange(nameof(Name));
        NotifyOfPropertyChange(nameof(IsAnnihilationNav));
        NotifyOfPropertyChange(nameof(HasAnnihilationStage));
        NotifyOfPropertyChange(nameof(ShowBattleIcon));
        NotifyOfPropertyChange(nameof(ShowNavIcon));
        NotifyOfPropertyChange(nameof(ShowAnnihilationOkIcon));
        NotifyOfPropertyChange(nameof(ShowAnnihilationFailIcon));
        NotifyOfPropertyChange(nameof(AnnihilationIconTip));
    }

    public string KindText => Model.Kind switch {
        CopilotSubTaskKind.Battle => LocalizationHelper.GetString("CopilotSubBattle"),
        CopilotSubTaskKind.Nav => LocalizationHelper.GetString("CopilotSubNav"),
        _ => Model.Kind.ToString(),
    };
}
