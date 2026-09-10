#include "ChapterNavigationTask.h"

#include "Task/Fight/StageNavigationTask.h"
#include "Task/Miscellaneous/ScreenshotTaskPlugin.h"
#include "Task/ProcessTask.h"
#include "Utils/Logger.hpp"

asst::ChapterNavigationTask::ChapterNavigationTask(const AsstCallback& callback, Assistant* inst) :
    InterfaceTask(callback, inst, TaskType),
    m_start_up_task_ptr(std::make_shared<ProcessTask>(callback, inst, TaskType)),
    m_chapter_navigation_task_ptr(std::make_shared<StageNavigationTask>(callback, inst, TaskType))
{
    LogTraceFunction;

    // 进入选关界面：与理智作战（FightTask）完全相同的做法，区别只是这里把"开始行动"等分支的次数限制为 0，
    // 保证只切页面、绝不会误开一局。
    m_start_up_task_ptr->set_times_limit("StartButton1", 0)
        .set_times_limit("StartButton2", 0)
        .set_times_limit("StoneConfirm", 0)
        .set_times_limit("StageSNReturnFlag", 0)
        .set_times_limit("PRTS1", 0)
        .set_times_limit("PRTS2", 0)
        .set_times_limit("PRTS3", 0)
        .set_times_limit("EndOfAction", 0)
        .set_retry_times(5);
    m_start_up_task_ptr->register_plugin<ScreenshotTaskPlugin>();
    m_start_up_task_ptr->set_tasks({ "StageBegin" }).set_times_limit("GoLastBattle", 0);

    m_subtasks.emplace_back(m_start_up_task_ptr);
    m_subtasks.emplace_back(m_chapter_navigation_task_ptr);
}

bool asst::ChapterNavigationTask::set_params(const json::value& params)
{
    LogTraceFunction;

    const int chapter = params.get("chapter", -1);
    if (chapter < 0) {
        LogError << __FUNCTION__ << "chapter not found";
        return false;
    }

    if (!m_running) {
        m_start_up_task_ptr->set_tasks({ "StageBegin" }).set_times_limit("GoLastBattle", 0);
    }

    return m_chapter_navigation_task_ptr->set_chapter(chapter);
}
