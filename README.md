# JuliMvs

冲压机视觉上位机工程。当前技术栈：

```text
C# / .NET 8 / WPF
OpenCVSharp
Hikvision MVS SDK
SQLite
xUnit
```

## 工程结构

```text
src/JuliMvs.App            WPF 主程序
src/JuliMvs.Core           领域模型、视觉几何、接口
src/JuliMvs.Vision         OpenCV 视觉算法
src/JuliMvs.Camera.Hik     海康 MVS 相机封装
src/JuliMvs.Plc            Modbus TCP PLC通信和模拟实现
src/JuliMvs.Persistence    SQLite 数据存储
tests/JuliMvs.Core.Tests   核心逻辑测试
tests/JuliMvs.Vision.Tests OpenCV 算法测试
tests/JuliMvs.Plc.Tests    Modbus TCP PLC通信测试
```

## 构建

```powershell
dotnet build JuliMvs.sln -c Release
```

## 测试

```powershell
dotnet test JuliMvs.sln -c Release
```

## 运行

```powershell
dotnet run --project src\JuliMvs.App\JuliMvs.App.csproj
```

## 当前功能

```text
技术员换型：建立/加载每种工件的标准位和模板
联合标定：同一组9张标定板图可完成畸变标定和9点XY标定
R轴中心标定：基于标定板中心点，不随工件型号保存
相机拍照建模板，模板保存标定来源，标定变化后自动失效
输出 OK / NG，检测OK时输出最终 X/Y/R 三轴动作量
计算视觉偏差、Home2D动作量、PLC最终输出，日志可追溯
支持 X/Y/R 补偿方向机器全局反向配置
支持PLC输出坐标系矩阵，默认不交换、不缩放、不偏置
支持9点XY标定，启用后用标定平面坐标计算X/Y偏差和宽高尺寸
PLC标准握手：D1000=1触发，OK写X/Y/R和D1010=1，NG写D1010=2
上位机不写D1010=0；写完D1010结果后由上位机主动清D1000=0
正式生产只拍一次，不等待PLC动作完成，不复拍，不做二次判定
保存检测记录到 SQLite
保存检测结果图
枚举、连接、拍照海康 MVS 相机
保存相机拍照元数据 JSON
离线批量检测并导出 CSV，用于调试和参数分析
```

## 后续硬件联调前必须确认

```text
相机型号、镜头和光源方案
相机触发方式：软触发或硬触发
PLC 品牌、型号和通信协议
PLC 地址表：D1000触发、D1002/D1004/D1006动作量、D1010结果、D1020/D1022产量、D1030型号
PLC float字序：默认低字在前，D1002-D1003/D1004-D1005/D1006-D1007各占2个寄存器
上位机写完D1010结果后主动清D1000=0，PLC侧按D1010读取本轮结果
9点XY标定、R轴中心标定、每种工件标准位/模板、X/Y/R方向和PLC输出坐标系
位置公差、角度公差、尺寸公差、面积/形状阈值和验收样件
```
