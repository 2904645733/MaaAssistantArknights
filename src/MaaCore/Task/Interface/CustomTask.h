#pragma once
#include "Task/InterfaceTask.h"

namespace asst
{
class ProcessTask;

class CustomTask final : public InterfaceTask
{
public:
    inline static constexpr std::string_view TaskType = "Custom";

    CustomTask(const AsstCallback& callback, Assistant* inst);
    virtual ~CustomTask() override = default;
    virtual bool set_params(const json::value& params) override;

    bool parse_and_register_secretfront(const std::string& task_name, std::string& resolved_task);

    bool parse_and_register_auto_raise_potential(const std::string& task_name, const json::value& params);

    bool parse_and_register_pixel_paint(const std::string& task_name, const json::value& params);

    bool parse_and_register_material_synthesis(const std::string& task_name);

    // 新手教程：进度识别插件（一轮一次 OCR + 查表，见 TutorialProgressTaskPlugin）
    bool parse_and_register_tutorial_progress(const std::string& task_name);

private:
    std::shared_ptr<ProcessTask> m_custom_task_ptr = nullptr;
};
}
