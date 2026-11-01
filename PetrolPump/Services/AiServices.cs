using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ZaibPetroleumService.Services
{
    public class AiResult
    {
        public string ApiAnswer { get; set; }
        public bool HasApiAnswer { get; set; }

        public static AiResult FromApi(string answer) =>
            new AiResult { ApiAnswer = answer, HasApiAnswer = true };

        public static AiResult Skipped() =>
            new AiResult { HasApiAnswer = false };
    }

    public static class AiPromptBuilder
    {
        public static string Build(string question, GeminiQueryMode mode, string extraContext = null)
        {
            switch (mode)
            {
                case GeminiQueryMode.Software:
                    return
                        "You are ZAIB PETROLEUM SERVICE AI Assistant — official helper of Zaib Petroleum Service petrol pump software.\n" +
                        "Answer ONLY in Urdu/English mix (Roman Urdu is fine). Be clear and helpful.\n" +
                        "RULES:\n" +
                        "1. User ne /software likha hai — sirf software ke bare mein jawab do (forms, buttons, workflow, reports).\n" +
                        "2. Use ONLY the SOFTWARE GUIDE below — do NOT invent forms or features.\n" +
                        "3. Batao kaunsa menu/button use karna hai, kis form mein kya hota hai.\n" +
                        "4. Agar sawal database numbers ka hai to user ko /database command use karne ko kahein.\n" +
                        "5. Short steps mein explain karo.\n\n" +
                        "SOFTWARE GUIDE:\n" + (extraContext ?? SoftwareKnowledgeContext.BuildContext()) + "\n\n" +
                        "USER QUESTION: " + question;

                case GeminiQueryMode.Database:
                    return
                        "You are ZAIB PETROLEUM SERVICE AI Assistant — official helper of Zaib Petroleum Service petrol pump software.\n" +
                        "Answer ONLY in Urdu/English mix (Roman Urdu is fine). Be short and clear.\n" +
                        "RULES:\n" +
                        "1. User ne /database likha hai — live database numbers se jawab do.\n" +
                        "2. Use ONLY the database numbers provided below — do NOT make up figures.\n" +
                        "3. User may say FORM NAME (e.g. Daily Diesel Sales, Expense, Closing 2) — map to the data sections.\n" +
                        "4. Do all math yourself from the given numbers (averages, profit, totals).\n" +
                        "5. If data is missing, say clearly ke database mein ye data nahi mila.\n" +
                        "6. Format amounts with commas, rates with 3 decimals.\n\n" +
                        "DATABASE DATA:\n" + (extraContext ?? "") + "\n\n" +
                        "USER QUESTION: " + question;

                default:
                    return
                        "You are ZAIB PETROLEUM SERVICE AI Assistant — official helper of Zaib Petroleum Service petrol pump software.\n" +
                        "Your name / identity: Zaib Petroleum Service AI.\n" +
                        "Answer in Urdu/English mix (Roman Urdu is fine). Be clear, friendly, and helpful.\n" +
                        "RULES:\n" +
                        "1. User ne koi special command nahi diya — koi bhi general sawal ka FULL jawab do (science, math, history, daily life, advice, etc.).\n" +
                        "2. Agar user pooche \"tum kaun ho / aap kaun ho / introduce yourself\" to clearly kaho:\n" +
                        "   \"Main Zaib Petroleum Service AI Assistant hoon — Zaib Petroleum Service petrol pump software ka helper.\"\n" +
                        "3. Software forms/buttons ki detail chahiye ho to short hint dein: /software use karein.\n" +
                        "4. Live sales/profit/balance numbers chahiye hon to short hint dein: /database use karein.\n" +
                        "5. General sawalon mein invent mat karo ke aap Google/ChatGPT ho — identity hamesha Zaib Petroleum Service AI rakho.\n" +
                        "6. Accurate aur useful jawab do; short aur clear rakho.\n\n" +
                        "USER QUESTION: " + question;
            }
        }

        public static int MaxTokens(GeminiQueryMode mode) =>
            mode == GeminiQueryMode.Software ? 1536 :
            mode == GeminiQueryMode.General ? 1200 : 1024;
    }

    public enum AiProviderType
    {
        Google,
        Groq
    }

    public static class GroqConfig
    {
        private static readonly string ExtraKeysFile = Path.Combine(
            LocalAppStorage.GetLocalDataDirectory(), "groq_keys.txt");

        // Groq models change — preferred + fallbacks
        private static readonly string[] DefaultModels =
        {
            "openai/gpt-oss-20b",
            "allam-2-7b",
            "groq/compound-mini",
            "qwen/qwen3.6-27b"
        };

        public static string Model =>
            ConfigurationManager.AppSettings["GroqModel"] ?? DefaultModels[0];

        public static string[] GetModelsToTry()
        {
            var list = new List<string>();
            string preferred = Model;
            if (!string.IsNullOrWhiteSpace(preferred))
                list.Add(preferred.Trim());

            foreach (string m in DefaultModels)
            {
                if (!list.Contains(m))
                    list.Add(m);
            }
            return list.ToArray();
        }

        public static List<string> GetAllKeys()
        {
            var keys = new List<string>();

            string configKeys = ConfigurationManager.AppSettings["GroqApiKeys"] ?? "";
            foreach (string k in configKeys.Split(new[] { '|', ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string trimmed = k.Trim();
                if (!string.IsNullOrEmpty(trimmed) && !keys.Contains(trimmed))
                    keys.Add(trimmed);
            }

            if (File.Exists(ExtraKeysFile))
            {
                foreach (string line in File.ReadAllLines(ExtraKeysFile))
                {
                    string trimmed = line.Trim();
                    if (!string.IsNullOrEmpty(trimmed) && !trimmed.StartsWith("#") && !keys.Contains(trimmed))
                        keys.Add(trimmed);
                }
            }

            return keys;
        }

        public static void AddExtraKey(string apiKey)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
                return;

            apiKey = apiKey.Trim();
            var existing = GetAllKeys();
            if (existing.Contains(apiKey))
                return;

            string dir = Path.GetDirectoryName(ExtraKeysFile);
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            File.AppendAllText(ExtraKeysFile, apiKey + Environment.NewLine);
        }

        public static bool HasAnyKey() => GetAllKeys().Count > 0;

        public static bool IsValidKeyFormat(string key) =>
            !string.IsNullOrWhiteSpace(key) && key.StartsWith("gsk_", StringComparison.OrdinalIgnoreCase);
    }

    public static class AiProviderSettings
    {
        private static readonly string ProviderFile = Path.Combine(
            LocalAppStorage.GetLocalDataDirectory(), "ai_provider.txt");

        private static AiProviderType? _cached;

        public static AiProviderType CurrentProvider
        {
            get
            {
                if (_cached.HasValue)
                    return _cached.Value;

                string config = ConfigurationManager.AppSettings["AiProvider"] ?? "";
                if (string.Equals(config, "Groq", StringComparison.OrdinalIgnoreCase))
                    return Cache(AiProviderType.Groq);
                if (string.Equals(config, "Google", StringComparison.OrdinalIgnoreCase))
                    return Cache(AiProviderType.Google);

                if (File.Exists(ProviderFile))
                {
                    string saved = File.ReadAllText(ProviderFile).Trim();
                    if (string.Equals(saved, "Groq", StringComparison.OrdinalIgnoreCase))
                        return Cache(AiProviderType.Groq);
                    if (string.Equals(saved, "Google", StringComparison.OrdinalIgnoreCase))
                        return Cache(AiProviderType.Google);
                }

                return Cache(AiProviderType.Groq);
            }
        }

        public static bool HasUserSelectedProvider() => File.Exists(ProviderFile);

        public static void SetProvider(AiProviderType provider)
        {
            _cached = provider;
            string dir = Path.GetDirectoryName(ProviderFile);
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(ProviderFile, provider.ToString());
        }

        public static string ProviderLabel(AiProviderType provider)
        {
            switch (provider)
            {
                case AiProviderType.Groq: return "Groq AI";
                default: return "Google Gemini";
            }
        }

        public static string CurrentProviderLabel() => ProviderLabel(CurrentProvider);

        public static bool IsProviderReady(AiProviderType provider)
        {
            if (provider == AiProviderType.Groq)
                return GroqConfig.HasAnyKey();

            var keys = GeminiConfig.GetAllKeys();
            return keys.Count > 0 && GeminiModelResolver.IsValidKeyFormat(keys[0]);
        }

        public static bool IsCurrentProviderReady() => IsProviderReady(CurrentProvider);

        public static bool ShowProviderPicker(IWin32Window owner)
        {
            using (var dlg = new Form())
            {
                dlg.Text = "AI Provider Select Karein";
                dlg.Size = new System.Drawing.Size(420, 260);
                dlg.StartPosition = FormStartPosition.CenterParent;
                dlg.BackColor = System.Drawing.Color.FromArgb(32, 36, 61);
                dlg.FormBorderStyle = FormBorderStyle.FixedDialog;
                dlg.MaximizeBox = false;
                dlg.MinimizeBox = false;

                var lbl = new Label
                {
                    Text = "Kaunsa AI use karna hai?\nEk dafa select karein — baad mein Settings se change ho sakta hai.",
                    ForeColor = System.Drawing.Color.White,
                    Location = new System.Drawing.Point(16, 16),
                    Size = new System.Drawing.Size(380, 50),
                    Font = new System.Drawing.Font("Segoe UI", 10F)
                };

                var btnGroq = new Button
                {
                    Text = "Groq AI (Recommended)",
                    Location = new System.Drawing.Point(16, 80),
                    Size = new System.Drawing.Size(380, 48),
                    BackColor = System.Drawing.Color.FromArgb(16, 163, 127),
                    ForeColor = System.Drawing.Color.White,
                    FlatStyle = FlatStyle.Flat,
                    Font = new System.Drawing.Font("Segoe UI Semibold", 11F, System.Drawing.FontStyle.Bold)
                };

                var btnGoogle = new Button
                {
                    Text = "Google Gemini",
                    Location = new System.Drawing.Point(16, 140),
                    Size = new System.Drawing.Size(380, 48),
                    BackColor = System.Drawing.Color.FromArgb(66, 133, 244),
                    ForeColor = System.Drawing.Color.White,
                    FlatStyle = FlatStyle.Flat,
                    Font = new System.Drawing.Font("Segoe UI Semibold", 11F, System.Drawing.FontStyle.Bold)
                };

                AiProviderType picked = AiProviderType.Groq;
                btnGroq.Click += (s, e) => { picked = AiProviderType.Groq; dlg.DialogResult = DialogResult.OK; dlg.Close(); };
                btnGoogle.Click += (s, e) => { picked = AiProviderType.Google; dlg.DialogResult = DialogResult.OK; dlg.Close(); };

                dlg.Controls.AddRange(new Control[] { lbl, btnGroq, btnGoogle });
                if (dlg.ShowDialog(owner) != DialogResult.OK)
                    return false;

                SetProvider(picked);
                return true;
            }
        }

        private static AiProviderType Cache(AiProviderType provider)
        {
            _cached = provider;
            return provider;
        }
    }

    public class GroqApiService
    {
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        private int _keyIndex;
        private string _lastError;

        public async Task<AiResult> AskAsync(string userQuestion, GeminiQueryMode mode, string extraContext = null)
        {
            _lastError = null;
            var keys = GroqConfig.GetAllKeys();
            if (keys.Count == 0)
                return AiResult.FromApi("Groq API key nahi mili. Settings se key add karein.");

            if (!GroqConfig.IsValidKeyFormat(keys[0]))
                return AiResult.FromApi("Groq API key galat format mein hai (gsk_... chahiye).");

            string prompt = AiPromptBuilder.Build(userQuestion, mode, extraContext);
            int maxTokens = Math.Max(AiPromptBuilder.MaxTokens(mode), 512);
            string[] models = GroqConfig.GetModelsToTry();

            for (int attempt = 0; attempt < keys.Count; attempt++)
            {
                string apiKey = keys[_keyIndex % keys.Count];
                _keyIndex++;

                foreach (string model in models)
                {
                    try
                    {
                        string answer = await CallGroqAsync(apiKey, model, prompt, maxTokens).ConfigureAwait(false);
                        if (!string.IsNullOrWhiteSpace(answer))
                            return AiResult.FromApi(answer);
                    }
                    catch (GroqRetryableException ex)
                    {
                        _lastError = ex.Message;
                        continue;
                    }
                    catch (Exception ex)
                    {
                        _lastError = ex.Message;
                        continue;
                    }
                }
            }

            string hint = string.IsNullOrWhiteSpace(_lastError)
                ? "Groq se jawab nahi aaya. Internet / API key check karein."
                : "Groq error: " + TrimError(_lastError);
            return AiResult.FromApi(hint);
        }

        private static string TrimError(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return "unknown";
            try
            {
                var root = JObject.Parse(raw);
                string msg = root["error"]?["message"]?.ToString();
                if (!string.IsNullOrWhiteSpace(msg))
                    return msg.Length > 220 ? msg.Substring(0, 220) + "..." : msg;
            }
            catch { }
            return raw.Length > 220 ? raw.Substring(0, 220) + "..." : raw;
        }

        private static async Task<string> CallGroqAsync(string apiKey, string model, string prompt, int maxTokens)
        {
            string url = "https://api.groq.com/openai/v1/chat/completions";

            var body = new
            {
                model = model,
                messages = new[] { new { role = "user", content = prompt } },
                temperature = 0.2,
                max_tokens = maxTokens
            };

            string json = JsonConvert.SerializeObject(body);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            using (var request = new HttpRequestMessage(HttpMethod.Post, url))
            {
                request.Content = content;
                request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + apiKey);

                HttpResponseMessage response = await Http.SendAsync(request).ConfigureAwait(false);
                string responseText = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                    throw new GroqRetryableException(responseText);

                return ParseGroqResponse(responseText);
            }
        }

        private static string ParseGroqResponse(string json)
        {
            var root = JObject.Parse(json);
            var message = root["choices"]?[0]?["message"];
            string text = message?["content"]?.ToString();
            if (!string.IsNullOrWhiteSpace(text))
                return text.Trim();

            // Some models put answer in reasoning when content empty
            string reasoning = message?["reasoning"]?.ToString();
            if (!string.IsNullOrWhiteSpace(reasoning))
                return reasoning.Trim();

            string finish = root["choices"]?[0]?["finish_reason"]?.ToString();
            if (finish == "content_filter")
                return "Jawab block ho gaya (content filter). Question dobara likhein.";

            return null;
        }

        private class GroqRetryableException : Exception
        {
            public GroqRetryableException(string msg) : base(msg) { }
        }
    }

    public class AiChatService
    {
        private readonly GeminiApiService _gemini = new GeminiApiService();
        private readonly GroqApiService _groq = new GroqApiService();

        public async Task<AiResult> AskAsync(string userQuestion, GeminiQueryMode mode, string extraContext = null)
        {
            if (AiProviderSettings.CurrentProvider == AiProviderType.Groq)
                return await _groq.AskAsync(userQuestion, mode, extraContext).ConfigureAwait(false);

            return await _gemini.AskAsync(userQuestion, mode, extraContext).ConfigureAwait(false);
        }

        public string CurrentProviderLabel() => AiProviderSettings.CurrentProviderLabel();
    }
}
