#pragma once

#include "Task/AbstractTaskPlugin.h"

#include <array>
#include <optional>
#include <string>
#include <vector>

#include <opencv2/core/mat.hpp>

namespace asst
{
// 新手教程：每轮做一件事 ——
//
//   ① 看【在不在对话】：用【官方 BattleAvatarDialog.png】在官方 roi [70,30,150,500] 里匹配
//      （模板内容是屏幕左上角的 RHODES ISLAND 标志，见下面 DialogBoxTemplate 的注释。
//        注意：它【不是】头像图案 —— 我曾经以为它是头像，还擅自换成了别的判据，都错了）
//   ② 在对话 → 对对话框内的文字条做一次 OCR，【逐行】拿结果（不拼接 ——
//      拼接会把右侧「终端」按钮等无关文字混进来，把真正的对话污染掉）
//   ③ 拿认出的每一行（以及连续若干行的拼接）去规则表里查，按【相似度】取最佳：
//        命中且有动作 → 按顺序做那几个动作（点指定坐标 / 拖拽 / 等待）
//        命中但没动作 → 普通对话，点一下对话框把对话翻过去
//        没命中       → 也当普通对话点掉（新手教程里绝大多数都是普通对话）
//   ④ 不在对话 → 这一轮什么都不做
//
// 动作全部由本插件直接执行（ctrler()->click / swipe），不再依赖"任务次数上限 +
// 候选顺序"那一套间接控制 —— 那套会把 ProcessTask 的若干内部规则（JustReturn 无条件命中、
// 次数上限在回调之前检查、Runout 会结束整条任务、候选顺序决定谁被执行）卷进来，
// 是之前一系列问题的根源。
//
// 资源侧只需要三样东西（见 resource/tasks/Tutorial/Progress.json）：
//   Tutorial@Progress@Scan   哨兵：每轮必然命中，用来把本插件唤醒
//   Tutorial@Progress@Table  规则表载体（text 里一条一句）
//   Tutorial@Progress@Loop   外层轮询：哨兵 + 少数"必须靠模板匹配"的任务（跳剧情、抽卡跳过）
//
// 规则表一条的格式（由 新手教程\工具\生成规则表.py 生成，勿手改）：
//   <段名>|<句子>|<动作1> <动作2> ...
//   动作写法：R:x,y,w,h    点这个矩形的中心（按钮推荐）
//             C:x,y        点这个精确坐标
//             G:x1,y1,x2,y2,ms   从这里拖到那里
//             W:ms         等这么久
//   句子里的 '|' 与 ':' 都不会出现，所以分隔安全。
class TutorialProgressTaskPlugin final : public AbstractTaskPlugin
{
public:
    using AbstractTaskPlugin::AbstractTaskPlugin;
    virtual ~TutorialProgressTaskPlugin() override = default;

    virtual bool verify(AsstMsg msg, const json::value& details) const override;

    // CustomTask 注册插件时把正在跑的 ProcessTask 传进来 ——
    // 只有它能调 override_next 改任务链的走向（教程跑完时结束本段）。
    void set_progress_task(ProcessTask* ptr) { m_progress_task = ptr; }

protected:
    virtual bool _run() override;

private:
    // 规则表里的一条
    struct Rule
    {
        int order = 0;
        std::string segment;                    // 序章·上 / 序章·下 / 主界面（日志用）
        std::string text;                       // 句子原文（日志用）
        std::string normalized;                 // 归一化后的整句，匹配用
        std::vector<size_t> normalized_offsets; // normalized 的字符边界表（匹配时按字符比）
        size_t normalized_chars = 0;            // normalized 的字符数
        std::vector<std::string> actions;       // 动作序列（原始写法，执行时再解析）
    };

    bool load_rules();                                            // 读规则表（只读一次）
    std::vector<std::string> ocr_text_box(const Rect& roi) const; // 对一块文字框做 OCR，逐行返回
    const Rule* lookup(
        const std::vector<std::string>& lines,                    // 查表：逐行/连续行拼接 + 相似度
        double* score_out,
        std::string* ocr_out) const;
    void run_actions(const std::vector<std::string>& actions); // 按顺序执行动作
    // 结束本段教程：把外层轮询的 next 覆盖成"结束点"任务（next 为空），
    // 任务链随之结束、队列继续跑后面的任务（Copilot 打 0-1）。
    void finish_segment(std::string_view reason);
    // 剧情跳过：认到右上角「跳过 ▶」就点它 + 点确认弹窗。返回 true = 处理掉了，
    // 本轮不用再做别的（对话框识别留给下一轮）。
    bool try_skip_plot();
    bool click_point(int x, int y) const;
    bool swipe_from_to(int x1, int y1, int x2, int y2, int duration) const;
    bool dialog_frame_changed(const cv::Mat& image, const Rect& area); // 对话框那块画面变没变

    std::vector<Rule> m_rules;
    bool m_rules_loaded = false;
    size_t m_rules_max_bytes = 0; // 最长规则的长度（字节），用于裁剪拼接
    // 上一轮的对话框取样（用于判断"点掉了没有"）；空 = 还没采过
    cv::Mat m_prev_dialog_frame;
    int m_unchanged_clicks = 0; // 连点几次画面都没变（累积到阈值就去做文字匹配）
    // 抽卡跳过：点过招募干员后为 true，直到重新看到对话为止
    bool m_gacha_skip_active = false;
    int m_gacha_skip_rounds = 0;
    // 正在跑的 ProcessTask（CustomTask 注册时传入）。用来在教程跑完时
    // override_next 结束本段任务链。空 = 没传进来，那时只能记日志、不结束。
    ProcessTask* m_progress_task = nullptr;
    // 退结算：还剩几次要点。-1 = 还没决定过（只在"进入本段后第一次没看到对话"时才决定：
    // 那说明停在上一关的结算画面上，见 exit_settlement 的说明）。
    int m_settlement_clicks_left = -1;

    // ── "现在处于对话"的判据 ──────────────────────────────────
    // 按用户要求，用【官方 BattleAvatarDialog.png】判断是不是在对话；roi 也照抄官方任务
    // （resource/tasks/tasks.json 里 BattleAvatarDialog 的 [70,30,150,500]），不要自己改小。
    //
    // 事实记录（免得以后又搞错）：这个模板的内容【不是头像】，而是屏幕【左上角那个
    // RHODES ISLAND 标志】(103x16)；官方那份 roi 正好覆盖它。实测 5 张截图：
    //     主界面 / 寻访（非剧情）: 0.991 ~ 0.994 @(78,36)   -> 命中
    //     两张剧情屏             : 0.164 / 0.563            -> 不命中
    // 用户也确认：遮住那个标志之后就不该再点。日志里的 matched rect 会指出它到底匹配在哪，
    // 万一"遮住了还在点"，看那一行就知道它是不是匹到了别处。
    inline static constexpr std::string_view DialogBoxTemplate = "BattleAvatarDialog.png";
    inline static constexpr double DialogBoxThreshold = 0.8;
    // 官方 BattleAvatarDialog 的 roi，原样照抄
    inline static const Rect DialogBoxSearchRect = Rect(70, 30, 150, 500);
    // ★ 不要用官方的 rectMove。它的 [125,20,200,60] 是给"战斗中头像对话框"用的，
    //   位置和招募/编队屏不一样；套过来会点到屏幕左上角 (303,86)（踩过）。
    //   点击位置 = 匹配到的框自己的中心，见 _run()。

    // 文字框（OCR 用）：对话框【整框】，要能容纳多行文字。
    // ★★ 这两个值【是用户实测给的】，别自己量了替换掉 ★★
    //   用户给的原值（见 新手教程\MAA机制笔记.md）：
    //     头像在上 [196, 47, 871, 182]
    //     头像在下 [232, 492, 825, 148]
    //   高度都是 148~182，是"整个对话框"的量级 —— 一行汉字只占约 22px，
    //   所以这两个框都能装下 2~8 行文字。
    //
    //   ★ 我犯过的错（同一个毛病犯了三次，每次都是把通用框改成特例框）：
    //     · 上方框被我写成 [200,56,820,58]（高 58，我自己量的，只有一行半）
    //       还误当成"用户给的值"写进注释 —— 长句「乐章的旋律由此开始……」
    //       因此只能读到前半截，相似度到不了 70%，那条规则永远认不出来；
    //     · 下方框被我改成 [232,512,825,36]（想躲开旁边的横幅）→ 小对话框框不住；
    //     · 上方框又被我改成 [200,70,820,36]（想对准那两行）→ 一行半装不下。
    //   教训：框大一点点没有代价（多读进来的空白/边框 OCR 会忽略），
    //   框小了就会把字裁掉。要么用用户给的实测值，要么按"整个对话框"给，宁大勿小。
    inline static const Rect TopBoxRect = Rect(196, 47, 871, 182);
    // 下方框（对话框在屏幕下方时用，例如「干员已经成功加入出击小队……」）
    inline static const Rect BottomBoxRect = Rect(232, 492, 825, 148);

    // 判断"这句有没有被点掉"用的取样区：盖住整条对话框。
    // 不比对文字，只看这块像素有没有变 —— 换一句话两帧灰度差很大。
    inline static const Rect DialogFrameRect = Rect(196, 47, 860, 84);
    // 两帧平均绝对差超过这个值就算"变了"（换一句话通常几十，画面抖动只有个位数）。
    inline static constexpr double DialogChangeThreshold = 6.0;
    // 点了但画面没变，连续这么多轮才认定"这句要动手" —— 避开打字机动画/延迟出帧的误判。
    inline static constexpr int MaxUnchangedClicks = 2;

    // 兜底：表里没写动作时，点对话框内部中央把对话翻过去（两块框各一个）
    inline static const Point TopContinuePoint = Point(608, 70);
    inline static const Point BottomContinuePoint = Point(644, 566);

    // ── 抽卡动画的「跳过」 ──────────────────────────────────────
    // 点完招募干员会进抽卡动画，那屏没有对话框、也没有 BattleAvatarDialog 标志，
    // 所以插件本来会什么都不做、教程就卡在那儿。
    // 处理办法：点过招募干员之后进入"追跳过"状态 —— 只要还没重新看到对话，
    // 就一直点跳过区。看到对话就退出这个状态，回到正常流程。
    //
    // 坐标 = 官方 GachaSkip 的 roi（[1144,0,136,148]），取中心。
    inline static const Point GachaSkipPoint = Point(1212, 74);
    // 追跳过时两次点击之间等多久（抽卡动画要播一会儿）
    inline static constexpr unsigned GachaSkipIntervalMs = 800;
    // 最多追多少轮，防止认不到对话时无限点下去
    inline static constexpr int MaxGachaSkipRounds = 40;
    // 触发"进入追跳过状态"的规则文字（规则表里那条招募干员）
    inline static constexpr std::string_view GachaTriggerText = "招募干员";

    // ── 退结算画面 ──────────────────────────────────────────────
    // 接在自动战斗（Copilot）后面时，游戏停在【关卡结算画面】：那屏没有对话框、
    // 也没有 BattleAvatarDialog 标志，插件本来会什么都不做。
    // 官方的做法就是反复点屏幕右上角那个角落（ClickCornerUntilStartButton 的
    // specificRect [1260,100,10,10]），这里照抄同一个点。
    inline static const Point SettlementCornerPoint = Point(1265, 105);
    // 最多点几下。官方是按"认出开始按钮"停止的，教程这一屏没有那个按钮，
    // 所以这里用固定次数（结算/评级/掉落那几层，点几下就够了）。
    inline static constexpr int SettlementMaxClicks = 6;

    // ── 剧情跳过 ────────────────────────────────────────────────
    // 教程里有三段纯剧情演出（起名后的开场、序章·上结束、序章·下结束），
    // 那几屏【没有对话框、也没有 BattleAvatarDialog 标志】，插件本来会干等着。
    //
    // 判据：右上角有没有「跳过 ▶」按钮。有 = 在剧情里，先跳过再走正常流程。
    // 区域用官方的 SkipThePreBattlePlot roi，模板用自己的：
    //   ★ 官方 SkipThePreBattlePlot.png 带 maskRange [100,255]，掩膜后只剩约 1/3 像素
    //     参与比较，右上角那排高亮的资源数字/图标能和真按钮一样亮 —— 实测在
    //     「招募干员」那一屏上给出 score=1.000000（满分误匹配），把插件的点击抢掉了。
    //     自己的 TutorialPlotSkip.png（92x42、带深色背景、不加掩膜）实测：
    //     剧情屏 1.0000、招募干员屏 0.2868，阈值 0.8 干净分开。
    inline static const Rect PlotSkipSearchRect = Rect(1096, 0, 182, 131);
    inline static constexpr std::string_view PlotSkipTemplate = "TutorialPlotSkip.png";
    inline static constexpr double PlotSkipThreshold = 0.8;

    // 跳过之后的确认弹窗（官方 SkipThePreBattlePlotConfirm：roi + 掩膜都照抄）
    inline static const Rect PlotSkipConfirmSearchRect = Rect(721, 290, 149, 244);
    inline static constexpr std::string_view PlotSkipConfirmTemplate = "SkipThePreBattlePlotConfirm.png";
    inline static constexpr double PlotSkipConfirmThreshold = 0.8;
    inline static constexpr std::array<int, 2> PlotSkipConfirmMaskRange = { 35, 255 };
    // 点完「跳过」等弹窗淡入，以及认不到时重试几次
    inline static constexpr unsigned PlotSkipConfirmWaitMs = 700;
    inline static constexpr int PlotSkipConfirmRetry = 3;

    inline static constexpr std::string_view ScanTaskName = "Tutorial@Progress@Scan";
    inline static constexpr std::string_view TableTaskName = "Tutorial@Progress@Table";
    // 外层轮询任务名：教程跑完时把它的 next 覆盖成 EndTaskName，让任务链结束。
    inline static constexpr std::string_view LoopTaskName = "Tutorial@Progress@Loop";
    // 结束点任务（next 为空 = 任务链到此结束，见 Progress.json）
    inline static constexpr std::string_view EndTaskName = "Tutorial@Progress@Done";
    // 规则表里表示"本段教程跑完"的动作：X（在规则表里写成 X:段名，冒号后的段名只用于日志）
    inline static constexpr std::string_view FinishActionKind = "X";
    // 相似度匹配参数（离线验证见 新手教程\工具\验证相似度匹配.py）
    // ★ 单位一律【字符】，不是字节。
    //   踩过：曾经"统一用字节"来消掉字节/字符混比，结果引入了更严重的错误 ——
    //   汉字 3 字节，不同汉字常共享首字节（E4/E5/E6/E7 只有十几种），
    //   按字节做 LCS 会把半个汉字算成匹配：「点击选中干员」(18 字节) 和
    //   「点击首页按钮回到中央大厅」凭空公共 13 字节（13 不是 3 的倍数），
    //   13/18 = 72% 越过 70% 线判满分 → 去点了错的那条规则。
    //   现在对比和阈值都用字符数，见 common_length_chars()。
    inline static constexpr double SimilarityThreshold = 0.85;
    inline static constexpr size_t MinCommonChars = 2;  // 公共字符太少不参与比较
    inline static constexpr size_t MinRuleChars = 2;    // 太短的规则不参与（误命中风险高）
    inline static constexpr unsigned ActionGapMs = 350; // 连续动作之间的间隔
};
} // namespace asst
