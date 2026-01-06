using UnityEngine;
using UnityEditor;
using System.IO;
using Newtonsoft.Json.Linq; // 确保安装了 Newtonsoft.Json 包

public class JsonMaterialImporter : EditorWindow
{
    // 定义材质类型枚举
    public enum ShaderMode
    {
        Standard_PBR = 0, // 普通物理材质 (写实)
        Toon_Anime = 1,   // 卡通材质 (二次元)
        Custom = 2        // 自定义 (手动指定)
    }

    private string jsonFilePath;
    private ShaderMode shaderMode = ShaderMode.Standard_PBR;
    private Shader customShader; // 仅在 Custom 模式下使用
    private string outputFolder = "Assets/RestoredMaterials";

    // === 配置区域：在这里修改你项目实际使用的 Shader 名字 ===
    // 普通材质 Shader 名 (优先 URP，其次 Built-in)
    private readonly string[] standardShaderNames = { 
        "Universal Render Pipeline/Lit", 
        "Standard" 
    };
    
    // 卡通材质 Shader 名 (你可以把 LilToon 改成你用的 Shader，比如 "Toon/Basic")
    private readonly string[] toonShaderNames = { 
        "lilToon", 
        "Universal Render Pipeline/Simple Lit", 
        "Unlit/Texture" 
    };
    // ========================================================

    [MenuItem("Tools/JSON to Material Importer (Optimized)")]
    public static void ShowWindow()
    {
        GetWindow<JsonMaterialImporter>("Material Restore");
    }

    private void OnGUI()
    {
        GUILayout.Label("Restore Material (Smart Mode)", EditorStyles.boldLabel);
        GUILayout.Space(10);

        // 1. 选择 JSON 文件
        if (GUILayout.Button("Select JSON File", GUILayout.Height(30)))
        {
            jsonFilePath = EditorUtility.OpenFilePanel("Select Material JSON", "", "json");
        }
        
        // 显示文件路径（如果太长则截断）
        string displayPath = string.IsNullOrEmpty(jsonFilePath) ? "None" : ".../" + Path.GetFileName(jsonFilePath);
        EditorGUILayout.LabelField("File:", displayPath, EditorStyles.helpBox);

        GUILayout.Space(10);

        // 2. 选择材质模式 (普通 vs 卡通)
        shaderMode = (ShaderMode)EditorGUILayout.EnumPopup("Shader Mode", shaderMode);

        // 3. 根据模式显示不同的 UI
        Shader targetShader = null;

        if (shaderMode == ShaderMode.Custom)
        {
            // 自定义模式：手动拖拽
            customShader = (Shader)EditorGUILayout.ObjectField("Custom Shader", customShader, typeof(Shader), false);
            targetShader = customShader;
        }
        else
        {
            // 自动查找模式
            targetShader = FindShaderAuto(shaderMode);
            
            // 显示查找结果状态
            if (targetShader != null)
            {
                EditorGUILayout.HelpBox($"Auto-Detected: {targetShader.name}", MessageType.Info);
            }
            else
            {
                EditorGUILayout.HelpBox($"Shader not found! Please install URP or a Toon shader, or use Custom mode.", MessageType.Error);
            }
        }

        GUILayout.Space(20);

        // 4. 执行按钮
        GUI.enabled = !string.IsNullOrEmpty(jsonFilePath) && targetShader != null;
        if (GUILayout.Button("Create Material", GUILayout.Height(40)))
        {
            CreateMaterialFromJson(targetShader);
        }
        GUI.enabled = true;
    }

    // 辅助方法：根据模式自动查找项目里存在的 Shader
    private Shader FindShaderAuto(ShaderMode mode)
    {
        string[] candidates = mode == ShaderMode.Standard_PBR ? standardShaderNames : toonShaderNames;

        foreach (string shaderName in candidates)
        {
            Shader s = Shader.Find(shaderName);
            if (s != null) return s;
        }
        return null;
    }

    private void CreateMaterialFromJson(Shader shaderToUse)
    {
        string jsonContent = File.ReadAllText(jsonFilePath);
        
        try
        {
            JObject root = JObject.Parse(jsonContent);
            
            string matName = root["m_Name"]?.ToString();
            if (string.IsNullOrEmpty(matName)) matName = Path.GetFileNameWithoutExtension(jsonFilePath);

            if (!Directory.Exists(outputFolder)) Directory.CreateDirectory(outputFolder);
            string savePath = Path.Combine(outputFolder, matName + ".mat");

            // 创建材质
            Material newMat = new Material(shaderToUse);

            JToken savedProps = root["m_SavedProperties"];
            if (savedProps != null)
            {
                // --- Float ---
                RestoreFloats(newMat, savedProps["m_Floats"]);
                // --- Color ---
                RestoreColors(newMat, savedProps["m_Colors"]);
                // --- Texture ---
                RestoreTextures(newMat, savedProps["m_TexEnvs"]);
            }

            AssetDatabase.CreateAsset(newMat, savePath);
            AssetDatabase.SaveAssets();
            EditorGUIUtility.PingObject(newMat);
            Debug.Log($"<color=green>Success:</color> {savePath}");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Error parsing JSON: {e.Message}");
        }
    }

    private void RestoreFloats(Material mat, JToken token)
    {
        if (token == null) return;
        foreach (JProperty prop in token)
        {
            if (mat.HasProperty(prop.Name))
                mat.SetFloat(prop.Name, (float)prop.Value);
        }
    }

    private void RestoreColors(Material mat, JToken token)
    {
        if (token == null) return;
        foreach (JProperty prop in token)
        {
            JToken c = prop.Value;
            Color col = new Color((float)c["r"], (float)c["g"], (float)c["b"], (float)c["a"]);
            if (mat.HasProperty(prop.Name))
                mat.SetColor(prop.Name, col);
        }
    }

    private void RestoreTextures(Material mat, JToken token)
    {
        if (token == null) return;
        foreach (JProperty prop in token)
        {
            string propName = prop.Name;
            JToken texData = prop.Value;

            // 1. 设置 Tiling/Offset
            if (mat.HasProperty(propName))
            {
                JToken s = texData["m_Scale"];
                JToken o = texData["m_Offset"];
                if (s != null && o != null)
                {
                    mat.SetTextureScale(propName, new Vector2((float)s["X"], (float)s["Y"]));
                    mat.SetTextureOffset(propName, new Vector2((float)o["X"], (float)o["Y"]));
                }
            }

            // 2. 查找并关联贴图
            string textureName = texData["m_Texture"]?["Name"]?.ToString();
            if (!string.IsNullOrEmpty(textureName))
            {
                Texture foundTex = FindTextureInProject(textureName);
                if (foundTex != null && mat.HasProperty(propName))
                {
                    mat.SetTexture(propName, foundTex);
                }
            }
        }
    }

    private Texture FindTextureInProject(string name)
    {
        // 查找所有类型纹理
        string[] guids = AssetDatabase.FindAssets(name + " t:Texture");
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (Path.GetFileNameWithoutExtension(path).Equals(name, System.StringComparison.OrdinalIgnoreCase))
            {
                return AssetDatabase.LoadAssetAtPath<Texture>(path);
            }
        }
        return null;
    }
}
