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
//             J:文件名     用 resource/copilot 下这份作业打这一关（例如 J:main_00-01.json）
//   句子里的 '|' 与 ':' 都不会出现，所以分隔安全。
//
// ★ J: 为什么存在（这是本插件从"队列驱动"改成"句子驱动"的关键）：
//   以前"什么时候打哪一关"是【GUI 那 5 个任务的顺序】决定的：
//     ① 教程段 → ② Copilot(0-1) → ③ 教程段 → ④ Copilot(TR-1) → ⑤ 教程段
//   这个设计在"从中间接着跑"时必然错位 —— 因为队列位置是固定的，而游戏进度不是。
//   实机踩过：某轮只命中了 TR-1 段的 3 条规则（前面 0-1 早已跑过），于是 ① 这个位置
//   跑完 TR-1 段结束，队列推进到 ② —— 而 ② 装的是 0-1 的作业，结果在 TR-1 的
//   关卡详情页上用了 0-1 的三个部署点。
//   改成 J: 之后，打哪一关由【认到哪句话】决定，和队列位置无关：认到就自己打，
//   打完接着认下一句。GUI 侧只需要 1 个教程任务。
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
    // 用 resource/copilot 下的一份作业打当前这一关（规则表里的 J: 动作）。
    // 同步跑完一整场：开始行动 → 编队 → 部署 → 打完 → 退出结算。
    // 跑之前会把"退结算"的计数装上，这样回来时停在结算画面也能自动点掉。
    bool run_copilot_job(const std::string& job_filename);
    // 结束本段教程：把外层轮询的 next 覆盖成"结束点"任务（next 为空），
    // 任务链随之结束、队列继续跑后面的任务（Copilot 打 0-1）。
    void finish_segment(std::string_view reason);
    // 剧情跳过：认到右上角「跳过 ▶」就点它 + 点确认弹窗。返回 true = 处理掉了，
    // 本轮不用再做别的（对话框识别留给下一轮）。
    bool try_skip_plot();
    // ── 兜底进终端 + 终端里打关卡（用户要求，照搬官方理智作战那套）──────
    // 调官方任务链进终端：CloseAnnos 关弹窗 -> QuickSwitch@ToTerminal 切终端。
    // 两个链都带 #back，调完会自己回到调用方。返回 true = 本轮已处理。
    bool try_enter_terminal();
    // 在终端里认「关卡进度」图标（LevelProgressRect + TutorialLevelProgress.png）。
    // 命中就 OCR 读关卡名、记下来、点卡片进这一关。返回 true = 本轮已处理。
    bool try_pick_level_in_terminal();
    // 关卡详情页出来了就点「开始行动」（认官方 StartButton1），然后跑对应作业。
    // 返回 true = 本轮已处理。
    bool try_start_selected_level();
    bool click_point(int x, int y) const;
    bool swipe_from_to(int x1, int y1, int x2, int y2, int duration) const;
    bool dialog_frame_changed(const cv::Mat& image, const Rect& area); // 对话框那块画面变没变
    // 这一屏能不能看到终端那张主题卡上的「关卡进度」图标（= 在终端，且卡片在进度那一栏）
    bool is_level_progress_visible(const cv::Mat& image) const;
    // 关卡详情页出来没有 —— 直接复用官方任务 StartButton1（就是详情页的「开始行动」）
    bool is_stage_detail_opened(const cv::Mat& image) const;

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
    // 退结算：还剩几次要点右上角那个角落。
    // ★ 只有【刚跑完一场自动战斗】时才有值 > 0（由 run_copilot_job 打完时装上）。
    //   平时恒为 0 = 不退结算。
    //   踩过：原来初始 -1 + "第一次没对话就装 6 次"，结果主界面上也白点 6 下右上角
    //   （用户看到的现象："一直点最右边"）。官方的 ClickCorner 是有门控的
    //   （只在 EndOfAction 之后走），这里是同样的意思。
    int m_settlement_clicks_left = 0;

    // 兜底计时：连续多少毫秒没看到对话。看到对话就清零。
    // 超过 NoDialogFallbackMs 且不在终端 -> 调官方链路进终端。
    // ★ 为什么用计时而不是"一没对话就进终端"：结算/剧情/抽卡那几屏本来就没有对话，
    //   一没对话就跳转会把这些正常流程打断。计时等于"给了 2 秒还没动静才算真卡住"。
    unsigned m_no_dialog_ms = 0;
    // 已经进过终端了（认到过进度图标）。用来避免每轮都重复调 QuickSwitch。
    bool m_in_terminal = false;
    // 从终端进度卡读到的关卡名（形如 0-1 / TR-5）。空 = 还没读到。
    std::string m_pending_level;
    // 已经点了进度卡、等详情页出来
    bool m_waiting_stage_detail = false;
    // ── 编队页等对话（用户要求的顺序）────────────────────────────
    // 详情页点了「开始行动」之后进编队页，那里可能还有教程对话，得先做掉对话，
    // 确认稳定没对话了才启动作业。所以点完开始行动先进入这个状态：
    //   m_waiting_battle_start = true  + m_battle_start_level = 关卡名
    // 由 _run() 里那个分支负责"等连续 BattleStartDwellMs 没对话 -> 启动作业"。
    bool m_waiting_battle_start = false;
    std::string m_battle_start_level;
    // 编队页上【是否认出过对话】。用户给的两条互斥规则：
    //   编队页识别到对话 -> 处理掉 -> 【不启动作业】-> 回到大循环
    //   编队页没识别到对话 -> 启动作业
    // 所以这是个"一次性事实"：只要在编队页上认出过一次对话，这一关的作业资格就作废。
    bool m_formation_dialog_seen = false;
    // ── TR-2：不能起作业，改点区域进战斗（用户要求）──────────────────
    // 用户原话：TR-2 没办法用自动战斗处理，所以检测到进度卡的进度是 TR-2 时，
    //   进入到编队页面【没有对话的情况下不能启动 TR-2 的作业】，
    //   而是点击 (1065, 430, 81, 150) 区域进入战斗。
    // 之后的流程由规则表接管（TR-2 段那一串对话，含点坐标和拖拽部署）。
    // 撤销：删掉这两个成员 + _run() 里标了「TR-2 手动进战斗」的那一段。
    inline static const std::string_view ManualEntryLevel = "TR-2";
    inline static const Rect ManualEntryRect = Rect(1065, 430, 81, 150);
    bool m_manual_entry_done = false;
    // ── "回到大循环去终端"的显式状态 ─────────────────────────────
    // ★ 为什么要有它：用户要的是一条【连续的链】——
    //     编队页对话处理完 -> 回大循环 -> 识别到导航栏 -> 点它 -> 到终端
    //     -> 认进度卡 -> 点它 -> 详情页 -> 点开始行动 -> 编队页 -> ...
    //   中间任何地方弹出对话，都先处理对话，再继续这条链（和从主界面进终端一样）。
    //   以前没有这个状态，靠"没对话 2.4 秒就当成卡住、去进终端"来猜 ——
    //   那个兜底会插在这条链中间乱触发，表现就是"在编队页上就开始点导航栏"。
    //   现在改成：编队页对话处理完【显式】置 true，由它驱动"回终端"这一步；
    //   而那个 2.4 秒兜底只在【从没主动去过终端】时才用（见 _run 里的条件）。
    bool m_return_to_terminal = false;
    // 编队页上连续多久没对话才认为"确实没对话"（刚进去时对话框还在淡入，
    // 等一小会儿再下结论，免得把"还没画出来"当成"没有对话"）。
    inline static constexpr unsigned BattleStartDwellMs = 3000;
    // ── 进终端之后的加载等待 ─────────────────────────────────────
    // ★ 踩过：点完终端【立刻】检查进度卡，那时界面还在加载、卡片还没出来，
    //   于是判成"没进去"、标记 in_terminal=0，下一轮又去点导航栏 ——
    //   表现就是"点了导航栏到终端后，又开始识别导航栏"。
    //   官方 TerminalDefault 的 postDelay 是 3000，就是等加载用的。
    //   不过实测这台机器加载挺快（设 3000 之后约 2 秒进度卡就认到了），
    //   1200 够用，还能少让人干等。
    //   ★ 这个值只影响"最慢等多久才重新考虑进终端" —— 这期间 _run() 每轮
    //     照样认进度卡，认到就清零、立刻往下走，所以调小不会漏判。
    unsigned m_terminal_settle_ms = 0;
    inline static constexpr unsigned TerminalSettleMs = 1200;

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

    // ── 兜底：进终端 ────────────────────────────────────────────
    // 上面两个判据（跳过按钮 / 头像标志）都认不到时，游戏可能停在某个"插件不认识、
    // 但也没有对话"的屏幕上（活动弹窗挡着、误入别的页面等），教程就卡住了。
    //
    // 兜底办法：连续这么久都没看到对话，就按官方"理智作战"那套回到终端 ——
    // 终端是教程的"安全落脚点"：到了那里就能认出关卡进度、把当前这一关打掉。
    //   CloseAnnos             先关掉一堆活动弹窗（干扰项）
    //   QuickSwitch@ToTerminal 小房子快捷切终端（主界面直接点终端；不在就点开菜单再点）
    // 两个都是官方现成的任务链，带 #back，调完自己会回到调用方。
    inline static constexpr unsigned NoDialogFallbackMs = 2000;

    // 进终端用的官方主路径任务（理智作战 Fight.sub 就是它）：
    // 认各个 UI 主题的 Terminal.png（= 主界面右上角那个「终端」大按钮）。
    // 实测 UiTheme/Default/Terminal.png 在主界面上 0.9836@(901,120)，稳过 0.8。
    inline static constexpr std::string_view TerminalEntryTaskName = "Terminal-Entry";

    // 不在主界面时的那条路：官方的 QuickSwitch（点小房子 -> 点菜单里的终端）。
    //
    // ★ 直接用官方任务链 "QuickSwitch@ToTerminal"，【不要】自己写死坐标 + 硬等。
    //   官方那两步本身就是【模板匹配 + ClickSelf】，而且模板是能匹上的：
    //     QuickSwitch/Menu.png     官方实测 0.985501 @[247,20,44,34]  roi [197,0,144,134]
    //     QuickSwitch/Terminal.png 官方实测 0.968569 @[500,50,59,48]  roi [450,0,159,148]
    //   我也在保存下来的真实界面截图上复测过 Menu.png：0.9879 / 0.9991 / 0.9990 / 0.9991
    //   （之前我说"匹不上、只能点写死坐标"是测错了图，那 0.65 是别的屏）。
    //   写死坐标 + sleep(1200) 不但慢，还怕界面一改就点空。

    // ── 终端里的「关卡进度」──────────────────────────────────────
    // 终端界面正中间那张主题卡：上面是主题图标 + MAIN THEME，
    // 下面是「进度」+ 当前关卡名（实测 0-1 / 1-2 / 2-1 / TR-5 这种）。
    //
    // ★ 这两个 roi 与模板都是【用户实测给的】：
    //     进度图标 模板框2_555_113_51_47.png（用户框 = [555,113,51,47]）
    //     关卡名   roi [556,233,48,26]，内容是卡片下方的关卡名，例如 TR-2
    //
    //   ★★ 踩过的两个坑（都是"模板比搜索区大"这一类，OpenCV 直接匹不了、恒 miss 0）：
    //      1. 最初写死成 roi [544,102,74,75]（74x75），而当时的模板是 77x77 —— 模板更大；
    //      2. 用户的模板图【比他自己给的框大一圈边距】：
    //         框 [555,113,51,47] = 51x47，实际模板文件是 77x71（多裁了 26x24 的边距）。
    //      所以 roi 不能照抄那个 51x47，要按【模板实际尺寸】给，并留一点余量。
    //      现在的算法：模板中心 = 框中心 = (580,136)；roi = 以该中心为中心、
    //      模板尺寸各边 + 6px 余量 -> [536,95,89,83]。
    //      余量给够没代价（只是多匹配一点背景），给不够就会裁掉模板导致匹不上。
    inline static const Rect LevelProgressRect = Rect(536, 95, 89, 83);
    inline static constexpr std::string_view LevelProgressTemplate = "TutorialLevelProgress.png";
    inline static constexpr double LevelProgressThreshold = 0.8;
    inline static const Rect LevelNameRect = Rect(556, 233, 48, 26);

    // ── 关卡详情页的「开始行动」───────────────────────────────────
    // ★ 用自己的轻量任务，【不要】用官方的 StartButton1。
    //   官方 StartButton1 是给理智作战用的：它 next 里挂着 StartButton2 ->
    //   StartButton2WaitTime，而 StartButton2WaitTime 的 postDelay 是 40000（等 40 秒）。
    //   教程点完开始行动就该直接进战斗，套那条链会白等 40 秒；用户等不及按了停止，
    //   need_exit() 变 true，紧随其后的 copilot job 一启动就立刻退出。
    //   实机日志（就是这个坑）：
    //     sleep 40000                                    <- 卡在这里
    //     asst::stop | enter                             <- 用户按了停止
    //     start button click returned 1
    //     run copilot job for level 0-2
    //     copilot job finished: 0-2.json ret: 0          <- 一启动就退出
    //   Tutorial@ClickStartAction 就是"认「开始行动」-> 点它 -> next 为空"。
    inline static constexpr std::string_view StageDetailTaskName = "Tutorial@ClickStartAction";
    // 官方那个也叫来一份：只用于判断"关卡详情页开没开"（它是 OcrDetect，不下发 next，
    // 用 PipelineAnalyzer 探测不会触发那条 40 秒的链）。探测和点击分开，互不影响。
    inline static constexpr std::string_view StageDetailProbeTaskName = "StartButton1";

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
    // 规则表里表示"用这份作业打这一关"的动作：J:<作业文件名>（文件在 resource/copilot 下）。
    // 作业统一放那儿 —— 不去猜用户的临时/自定义路径（避免和用户自己的作业撞车）。
    inline static constexpr std::string_view CopilotActionKind = "J";
    inline static constexpr std::string_view CopilotResourceDir = "copilot";
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
