using System;
using System.Linq;
using Revolution.Editor;
using UnityEditor;
using UnityEngine;

namespace Revolution.RevAsset.Editor
{
    /// <summary>RevCLI 与 RevMCP 的编辑器入口，使用面板的真实规则和构建函数。</summary>
    public static class RevAssetAutomation
    {
        /// <summary>项目开关参数。</summary>
        [Serializable]
        public sealed class SelectInput
        {
            /// <summary>选择 RevAsset 或框架自带资源系统。</summary>
            public bool enabled;
            /// <summary>在 Play 用 AssetDatabase 模拟。</summary>
            [RevAutomationOptional] public bool simulation = true;
        }

        /// <summary>项目资源和分包配置中的单个规则。</summary>
        [Serializable]
        public sealed class RuleInput
        {
            /// <summary>Assets 内选择器。已有相同路径时更新该规则。</summary>
            public string path;
            /// <summary>NativeOrFolder、Together、ByFolder、ByAsset 或 Exclude。</summary>
            public string packing;
            /// <summary>合包规则的原生名称。</summary>
            [RevAutomationOptional] public string bundle;
            /// <summary>可选的原生变体。</summary>
            [RevAutomationOptional] public string variant;
            /// <summary>文件逻辑地址或目录的自定义前缀。</summary>
            [RevAutomationOptional] public string address;
            /// <summary>用于首包、下载和分发的标签。</summary>
            [RevAutomationOptional] public string[] tags;
        }

        /// <summary>更新项目共用运行和构建字段。未传字段保持项目现值。</summary>
        [Serializable]
        public sealed class ConfigureInput
        {
            /// <summary>用于自动推导地址的项目资源根。</summary>
            [RevAutomationOptional] public string addressRoot;
            /// <summary>客户端内容包名。</summary>
            [RevAutomationOptional] public string package;
            /// <summary>应用兼容代号。</summary>
            [RevAutomationOptional] public string compatibility;
            /// <summary>静态发布目录。</summary>
            [RevAutomationOptional] public string channel;
            /// <summary>允许验证频道的公钥。</summary>
            [RevAutomationOptional] public string publicKey;
            /// <summary>首包标签，空数组对应全量首包。</summary>
            [RevAutomationOptional] public string[] builtinTags;
        }

        /// <summary>显式构建输入。签名私钥路径仅用于本次动作。</summary>
        [Serializable]
        public sealed class BuildInput
        {
            /// <summary>可读的资源版本。</summary>
            public string version;
            /// <summary>递增发布序号。</summary>
            public int revision;
            /// <summary>目标 Unity BuildTarget。默认当前构建平台。</summary>
            [RevAutomationOptional] public string target;
            /// <summary>是否同时准备平台首包。</summary>
            [RevAutomationOptional] public bool builtin = true;
            /// <summary>频道发布目录，默认位于 Library。</summary>
            [RevAutomationOptional] public string destination;
            /// <summary>项目资产外部的 RSA XML 私钥位置。</summary>
            [RevAutomationOptional] public string signingKeyPath;
        }

        /// <summary>查询项目选择、地址根和内容包状态的编辑器回执。</summary>
        [Serializable]
        public sealed class StatusResult
        {
            /// <summary>下次装配使用的后端。</summary>
            public string backend;
            /// <summary>当前 Play 已装配的后端。</summary>
            public string active;
            /// <summary>选择了编辑器模拟。</summary>
            public bool simulation;
            /// <summary>项目内容包名。</summary>
            public string package;
            /// <summary>应用兼容代号。</summary>
            public string compatibility;
            /// <summary>自动地址的项目根目录。</summary>
            public string addressRoot;
            /// <summary>显式规则总数。</summary>
            public int rules;
            /// <summary>首包标签。</summary>
            public string[] builtinTags;
            /// <summary>当前编辑器构建目标。</summary>
            public string target;
            /// <summary>当前 Play 已挂载的内容版本。</summary>
            public string mounted;
        }

        /// <summary>分页预览采集结果，工程资源数量不影响单次命令输出的上限。</summary>
        [Serializable]
        public sealed class PlanInput
        {
            /// <summary>从哪个已排序条目开始返回。</summary>
            [RevAutomationOptional] public int offset;
            /// <summary>每类结果返回多少项。空值或零使用 50 项。</summary>
            [RevAutomationOptional] public int limit;
        }

        /// <summary>一项原生包的明确归属。</summary>
        [Serializable]
        public sealed class GroupResult
        {
            /// <summary>原生包名与变体。</summary>
            public string name;
            /// <summary>明确放进该包的资产数量。</summary>
            public int assets;
            /// <summary>本包可用于选择首包和预下载的标签。</summary>
            public string[] tags;
        }

        /// <summary>分包计划总量及所请求的一页内容。</summary>
        [Serializable]
        public sealed class PlanResult
        {
            /// <summary>候选包总数。</summary>
            public int bundles;
            /// <summary>候选逻辑地址总数。</summary>
            public int assets;
            /// <summary>从多个包归并的共享资产。</summary>
            public int sharedAssets;
            /// <summary>本页显示的原生包。</summary>
            public GroupResult[] groups;
            /// <summary>本页显示的实际逻辑路径及归属。</summary>
            public RevAssetAddressData[] addresses;
        }

        /// <summary>查询实际项目资源选择和当前规则。</summary>
        [RevAutomationAction("revasset.status", Environment = "either", ReadOnly = true, Keys = new string[0],
            OutputType = typeof(StatusResult), Description = "查询项目资源后端、模拟模式、内容包与分包规则")]
        public static StatusResult Status(RevAutomationRequest request)
        {
            RevAssetSettings settings = RevAssetEditorProject.GetSettings(false);
            var profile = RevAssetBuildProfile.instance;
            return new StatusResult
            {
                backend = RevAssetEditorProject.Selected ? RevAsset.BackendId : "builtin",
                active = RevResourceBackends.ActiveId,
                simulation = settings?.EditorSimulation ?? true,
                package = settings?.Package ?? "main",
                compatibility = settings?.Compatibility ?? "1",
                addressRoot = profile.AddressRoot,
                rules = profile.Rules.Count,
                builtinTags = profile.BuiltinTags,
                target = EditorUserBuildSettings.activeBuildTarget.ToString(),
                mounted = RevAsset.MountedVersion
            };
        }

        /// <summary>保存项目下一次 Play 与构建使用的资源策略。</summary>
        [RevAutomationAction("revasset.select", InputType = typeof(SelectInput), Description = "选择 RevAsset 或框架自带资源后端")]
        public static object Select(RevAutomationRequest request)
        {
            var input = Read<SelectInput>(request);
            RevAssetEditorProject.Select(input.enabled, input.simulation);
            return Status(request);
        }

        /// <summary>保存公开配置，分包规则仍由同一份项目文件提供。</summary>
        [RevAutomationAction("revasset.configure", InputType = typeof(ConfigureInput), Description = "设置资源根、包名、兼容代号、首包标签与热更新频道")]
        public static object Configure(RevAutomationRequest request)
        {
            var input = Read<ConfigureInput>(request);
            if ((input.package != null && !RevAssetFiles.IsId(input.package)) ||
                (input.compatibility != null && !RevAssetFiles.IsId(input.compatibility)) ||
                (input.builtinTags != null && input.builtinTags.Any(tag => !RevAssetFiles.IsId(tag))))
            {
                throw new ArgumentException("包名、兼容代号或首包标签无效");
            }
            var profile = RevAssetBuildProfile.instance;
            RevAssetSettings settings = RevAssetEditorProject.GetSettings(true);
            settings.Package = input.package ?? settings.Package;
            settings.Compatibility = input.compatibility ?? settings.Compatibility;
            settings.ChannelUrl = input.channel ?? settings.ChannelUrl;
            settings.PublicKey = input.publicKey ?? settings.PublicKey;
            profile.AddressRoot = input.addressRoot ?? profile.AddressRoot;
            profile.BuiltinTags = input.builtinTags ?? profile.BuiltinTags;
            profile.SaveProfile();
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            return Status(request);
        }

        /// <summary>应用文件或目录级规则，不修改原生 importer 标签。</summary>
        [RevAutomationAction("revasset.rule", InputType = typeof(RuleInput), Description = "保存或更新单条资源采集、地址和分包规则")]
        public static object Rule(RevAutomationRequest request)
        {
            var input = Read<RuleInput>(request);
            string path = (input.path ?? "").Replace('\\', '/').TrimEnd('/');
            if (!path.StartsWith("Assets/", StringComparison.Ordinal) && path != "Assets" ||
                !Enum.TryParse(input.packing, false, out RevAssetPacking packing) || packing.ToString() != input.packing)
            {
                throw new ArgumentException("分包规则需要 Assets 下的路径和有效打包方式");
            }
            if (!AssetDatabase.IsValidFolder(path) && string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(path)))
            {
                throw new ArgumentException("规则路径没有对应的 Unity 资产：" + path);
            }
            var profile = RevAssetBuildProfile.instance;
            int index = profile.Rules.FindIndex(rule => rule.Path.Replace('\\', '/').TrimEnd('/') == path);
            var value = new RevAssetBuildRule
            {
                Path = path, Packing = packing, Bundle = input.bundle ?? "", Variant = input.variant ?? "",
                Address = input.address ?? "", Tags = input.tags ?? new[] { "core" }
            };
            if (index < 0)
            {
                profile.Rules.Add(value);
            }
            else
            {
                profile.Rules[index] = value;
            }
            profile.SaveProfile();
            return Status(request);
        }

        /// <summary>列出完整资源归属与标签，供命令行和 CI 核对。</summary>
        [RevAutomationAction("revasset.plan", ReadOnly = true, InputType = typeof(PlanInput), OutputType = typeof(PlanResult),
            Description = "预览资源采集、包归属、地址、标签与隐式共享依赖")]
        public static PlanResult Plan(RevAutomationRequest request)
        {
            var input = Read<PlanInput>(request);
            int offset = Math.Max(input.offset, 0);
            int limit = input.limit <= 0 ? 50 : Math.Min(input.limit, 500);
            var plan = RevAssetCollector.Collect(RevAssetBuildProfile.instance, true);
            return new PlanResult
            {
                bundles = plan.Bundles.Count, assets = plan.Addresses.Count, sharedAssets = plan.SharedAssets,
                groups = plan.Bundles.Skip(offset).Take(limit).Select(bundle => new GroupResult
                {
                    name = bundle.Key, assets = bundle.Value.Assets.Count, tags = bundle.Value.Tags.ToArray()
                }).ToArray(),
                addresses = plan.Addresses.Skip(offset).Take(limit).ToArray()
            };
        }

        /// <summary>打包或准备首包，失败原因通过工单返回。</summary>
        [RevAutomationAction("revasset.build", InputType = typeof(BuildInput),
            Description = "使用原生 AB 构建并准备首包或签名频道")]
        public static RevAssetBuildResult Build(RevAutomationRequest request)
        {
            var input = Read<BuildInput>(request);
            BuildTarget target = EditorUserBuildSettings.activeBuildTarget;
            if (!string.IsNullOrEmpty(input.target) &&
                (!Enum.TryParse(input.target, false, out target) || target.ToString() != input.target))
            {
                throw new ArgumentException("不支持的 Unity BuildTarget：" + input.target);
            }
            return RevAssetBuilder.Build(target, input.version, input.revision, input.builtin, input.destination, input.signingKeyPath);
        }

        /// <summary>通过 Unity 序列化读取已按 DTO 契约校验的参数。</summary>
        private static T Read<T>(RevAutomationRequest request) where T : class, new()
        {
            return JsonUtility.FromJson<T>(request.payloadJson) ?? new T();
        }
    }
}
