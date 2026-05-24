关卡配置（Level Definition）

1. 在 Project 窗口右键：Create → Hemiao → Level Definition
2. 创建 Level_01 … Level_10 共 10 个资源，放入本文件夹
3. 在玩法场景中新建空物体 LevelManager，挂载 LevelManager 脚本：
   - Levels：按顺序拖入 10 个 LevelDefinition
   - Item Spawner：拖入场景里的 ItemSpawner
   - Props Root：空物体 LevelRoot/Props（静态道具会生成在其下）
4. 将 ItemSpawner 的 Auto Start 取消勾选（由关卡表控制何时开始掉落）

每个 LevelDefinition 可配置：
- Spawn Prefabs：本关掉落物预制体列表
- Scene Props：本关场上道具（Prefab + Spawn Point 或本地坐标）

关卡流程（3test 场景）：
- LevelSession + LevelHubUI：进入场景先显示准备界面（关卡/余额/债务/进入关卡/归还债务）
- 点击「进入关卡」后开始掉落与倒计时；倒计时结束再次回到准备界面
- CountDownTimer 的 startOnAwake 应保持关闭，由 LevelSessionController 驱动

每个 LevelDefinition 还可配置：
- levelDurationSeconds：本关倒计时时长（秒）

过关时也可代码调用：LevelManager.Instance.LoadNextLevel();
或指定关卡：LevelManager.Instance.LoadLevel(2);
