# 地质填图桌面系统（GeologicalMappingDesktop）

一套面向数字地质填图的桌面端应用，覆盖 **外业数据采集 → 云端 / 局域网同步 → 内业数据编辑 → 图件编绘 → 成果输出** 的完整业务流程。桌面端基于 **C# / WPF / .NET Framework 4.7.2 + ArcGIS Runtime 100.15.3** 开发，配套 Android 野外采集端（Kotlin + ArcGIS Runtime）。

![platform](https://img.shields.io/badge/platform-Windows-blue)
![.NET](https://img.shields.io/badge/.NET%20Framework-4.7.2-purple)
![WPF](https://img.shields.io/badge/UI-WPF-512BD4)
![ArcGIS%20Runtime](https://img.shields.io/badge/ArcGIS%20Runtime-100.15.3-0079C1)

---

## 一、项目简介

地质填图传统上依赖纸质记录与手工转绘，外业与内业数据割裂、效率低、易出错。本系统以 ArcGIS Runtime 为地图引擎，结合地质行业的数据分类与符号体系，打通移动端野外采集与桌面端室内成图的数据链路，让不熟悉通用 GIS 软件的地质人员也能直接开展数字化填图作业。

- **桌面端**：数据编辑、图层管理、图件编绘、野外数据整理、空间校准、拓扑检查、成果导出
- **移动端**：野外点位 / 路线采集、GPS 定位、照片 / 素描 / 视频记录
- **同步**：支持局域网直连（桌面端内置 HTTP 服务）与云端同步（Node.js / Express 后端）两条链路

---

## 二、四大功能模块

桌面端左侧导航划分为四个模块，顶部工具栏提供项目与地图的全局操作。

### 1. 图层管理
- 多数据源加载：Shapefile、栅格影像（TIF / JPG / PNG / BMP）、文件地理数据库
- 图层树：显示 / 隐藏、顺序调整、透明度、图层属性设置
- 无参考影像支持：无坐标的 JPG / PNG 以覆盖层方式叠加显示，供后续空间校准
- 图层符号渲染与图例

### 2. 数据编辑
地质空间数据编辑模块，是系统的核心：
- **要素绘制**：点 / 线 / 面绘制，支持手绘流式绘制、方位-距离精确输入、顶点捕捉（Snapping）
- **要素编辑**：顶点编辑（增删 / 拖拽）、要素移动、删除、拓扑锁定
- **选择工具**：点选、框选、Shift 多选、按属性选择
- **属性处理**：属性表、字段增删、字段计算器、批量更新属性、批量纠正
- **撤销 / 重做**：基于编辑操作栈（EditOperation）的多步撤销
- **空间校准（地理配准）**：控制点 + 仿射 / 相似变换，实时残差可视化，支持导出 GeoTIFF / World File
- **坐标系投影转换**：基于 GeometryEngine.Project 的图层 / 文件坐标转换
- **拓扑检查**：悬挂点、重叠、缝隙、自相交等拓扑错误检测与定位

### 3. 图件编绘
地质图编绘模块：
- 版式视图与图幅整饰：图廓、坐标网格 / 经纬网、图例、比例尺、指北针
- 图件要素拖拽布局与属性调整
- 成图渲染与导出

### 4. 野外数据
野外数据接收与整理模块：
- 导入移动端导出的 ZIP 数据包
- 解析 GeoJSON，自动生成点 / 线图层
- 按点位 ID 自动匹配照片、素描、视频等附件并归档
- 样点清单与属性查看
- 导出 CSV / GeoJSON

### 顶部工具栏
项目新建 / 打开 / 保存、添加数据、地图缩放 / 全图、撤销 / 重做、版本管理、登录与云同步。

---

## 三、技术栈

| 层次 | 技术 |
| --- | --- |
| 桌面 UI | C# / WPF / XAML（.NET Framework 4.7.2） |
| 地图引擎 | Esri ArcGIS Runtime 100.15.3（WPF） |
| 桌面端同步服务 | `HttpListener`（局域网）+ `WebClient`（云端） |
| 移动端 | Kotlin + ArcGIS Runtime 100.15.8（Android，minSdk 24） |
| 云端后端 | Node.js + Express（JWT 鉴权，数据 / 附件同步） |
| 数据格式 | GeoJSON、Shapefile、GeoTIFF、CSV |
| 压缩 / 解析 | System.IO.Compression（ZIP）、轻量自研 JSON 解析 |

---

## 四、系统架构与数据流

```
┌─────────────────────────┐        ┌──────────────────────────┐
│   Android 野外采集端     │        │   Node.js / Express 后端  │
│  (Kotlin + ArcGIS)      │◀──────▶│  /api/auth  /api/sync/*   │
│  点位 / 路线 / 多媒体    │  云端   └────────────┬─────────────┘
└───────────┬─────────────┘                     │
            │ 局域网 (HttpListener)              │ 云端
            │                                    │
            ▼                                    ▼
┌────────────────────────────────────────────────────────────┐
│                 地质填图桌面系统 (WPF)                       │
│  UI 层：各 Window / Panel（图层管理 / 数据编辑 / 编绘 / 野外）│
│  业务层：FieldDataHelper · GeoJsonHelper · GeoreferenceHelper│
│          ExportHelper · PatternFillHelper · SyncService      │
│  引擎层：ArcGIS Runtime（Map / MapView / FeatureTable / Symbol）│
│  持久层：.gmp 项目文件 · GeoJSON · Shapefile · GeoTIFF · 附件 │
└────────────────────────────────────────────────────────────┘
```

**数据流**：野外采集 → 导出 GeoJSON + 多媒体 ZIP → 局域网 / 云端同步 → 桌面端解析入库 → 编辑与符号化 → 配准 / 拓扑检查 → 编绘出图 → 成果导出。

---

## 五、目录结构

```
GeologicalMappingDesktop/
├── GeologicalMappingDesktop.sln          # 桌面端解决方案
├── GeologicalMappingDesktop/             # 桌面端（WPF）
│   ├── MainWindow.xaml(.cs)              # 主窗口：模块导航 / 地图 / 工具栏
│   ├── LayerManagerPanel.xaml(.cs)       # 图层管理
│   ├── DataEditPanel.xaml(.cs)           # 数据编辑
│   ├── FieldDataPanel.xaml(.cs)          # 野外数据
│   ├── MapCompileWindow.xaml(.cs)        # 图件编绘
│   ├── TopologyCheckPanel.xaml(.cs)      # 拓扑检查
│   ├── CoordinateProjectionWindow.*      # 坐标投影转换
│   ├── GeoreferenceToolbarWindow.*       # 空间校准
│   ├── Lithology/Line/PointSymbol*       # 地质符号系统
│   ├── FieldDataHelper.cs                # 野外数据导入 / 附件匹配
│   ├── GeoJsonHelper.cs                  # GeoJSON 解析与导出
│   ├── GeoreferenceHelper.cs             # 配准变换与残差
│   ├── GeoTiffWriter.cs                  # GeoTIFF 写出
│   ├── ExportHelper.cs                   # Shapefile / 成果导出
│   ├── ProjectManager.cs                 # 项目文件与版本管理
│   └── SyncService.cs                    # 局域网同步服务
└── geomappingmobile/                     # Android 野外采集端（Kotlin）
    └── app/src/main/java/com/example/geomappingmobile/
        ├── ui/map|collection|route/      # 地图 / 采集 / 路线界面
        ├── service/                      # 定位 / 同步服务
        └── util/                         # GeoJSON / GPX / ZIP 工具
```

---

## 六、环境要求

- Windows 10 / 11（x64）
- Visual Studio 2019 / 2022（含 .NET 桌面开发工作负载）
- .NET Framework 4.7.2 Developer Pack
- NuGet 包：`Esri.ArcGISRuntime` / `Esri.ArcGISRuntime.WPF` 100.15.3
- ArcGIS Runtime 授权（ApiKey 或 License）

> Android 端需 Android Studio 与 JDK 11；云端后端为独立 Node.js 服务。

---

## 七、快速开始

```bash
# 1. 克隆仓库
git clone https://github.com/BethHu/GeologicalMappingDesktop.git

# 2. 用 Visual Studio 打开解决方案
#    GeologicalMappingDesktop/GeologicalMappingDesktop.sln

# 3. 还原 NuGet 包（右键解决方案 → 还原 NuGet 程序包）

# 4. 配置 ArcGIS Runtime ApiKey 后编译运行（Debug 目标平台为 x64）
```

---

## 八、使用流程

1. **新建项目**：工具栏「新建」，指定项目目录，生成 `.gmp` 项目文件与数据目录。
2. **加载数据**：「添加」加载 Shapefile / 栅格 / 地理数据库，或从「野外数据」导入移动端 ZIP。
3. **编辑要素**：「数据编辑」中绘制 / 修改点线面，管理字段与属性，必要时做拓扑检查与坐标转换。
4. **空间校准**：对无坐标影像添加控制点完成地理配准。
5. **图件编绘**：「图件编绘」中整饰图幅、添加图例 / 网格 / 比例尺，导出成图。
6. **同步 / 输出**：登录云同步或局域网同步野外数据，导出 Shapefile / GeoJSON / GeoTIFF / CSV。

---

## 九、关键技术点

- **地质符号系统**：内置 30 余种岩性 / 地层花纹与点线符号，支持按岩性代号自动赋符号
- **空间校准**：控制点最小二乘变换与残差可视化，支持 GeoTIFF / World File 导出
- **双视图联动**：地图视图与数据视图同步，叠加层随标签页切换
- **轻量 JSON 解析**：自研流式 GeoJSON 解析，降低外部依赖
- **多端数据契约**：桌面端与移动端统一的 GeoJSON 字段规范（pointType / lineType / status）

---

## 十、配套移动端

本仓库 `geomappingmobile/` 为 Android 野外采集端，功能包括：地图浏览、GPS 定位、地质点 / 路线采集、照片 / 素描 / 视频记录、离线瓦片、GeoJSON / GPX / ZIP 导出与云端同步。

---

## 十一、后续计划

- 引入 MVVM 重构 UI 层，补充单元测试
- 优化大数据量要素渲染与内存管理
- 完善云端同步的冲突解决与增量同步
- 增加更多行业符号标准与成果模板

---

## 十二、许可证

本项目仅用于学习与研究。使用 ArcGIS Runtime 需遵守 Esri 相关授权条款。
