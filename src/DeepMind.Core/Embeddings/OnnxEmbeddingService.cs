using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.ML.Tokenizers;
using Microsoft.Extensions.Logging;
using DeepMind.Core.Configuration;

namespace DeepMind.Core.Embeddings;

public class OnnxEmbeddingService : IDisposable
{
    private InferenceSession? _session;
    private BertTokenizer? _tokenizer;
    private readonly ILogger<OnnxEmbeddingService> _logger;
    private readonly EmbeddingConfig _config;

    public bool IsAvailable => _session != null;
    public int Dimensions => _config.Dimensions;

    public OnnxEmbeddingService(EmbeddingConfig config, ILogger<OnnxEmbeddingService> logger)
    {
        _config = config;
        _logger = logger;

        var modelPath = ResolveModelPath(config.ModelPath, "model.onnx");
        var vocabPath = ResolveModelPath(
            Path.Combine(Path.GetDirectoryName(config.ModelPath)!, "vocab.txt"),
            "vocab.txt");

        // Load tokenizer
        if (vocabPath != null && File.Exists(vocabPath))
        {
            try
            {
                _tokenizer = BertTokenizer.Create(vocabPath);
                _logger.LogInformation("Tokenizer loaded from {VocabPath}", vocabPath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load tokenizer. Falling back to simple tokenization.");
            }
        }

        // Load ONNX model
        if (modelPath != null && File.Exists(modelPath))
        {
            try
            {
                _session = new InferenceSession(modelPath);
                _logger.LogInformation("Embedding model loaded: {ModelName} ({Dimensions}d) from {ModelPath}",
                    config.ModelName, config.Dimensions, modelPath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load embedding model. Vector search disabled.");
            }
        }
        else
        {
            _logger.LogWarning("Embedding model not found. Vector search disabled.");
        }
    }

    public float[]? GenerateEmbedding(string text)
    {
        if (_session == null) return null;

        try
        {
            var (inputIds, attentionMask) = Tokenize(text);

            var idsTensor = new DenseTensor<long>(new[] { 1, inputIds.Length });
            var maskTensor = new DenseTensor<long>(new[] { 1, inputIds.Length });
            var typeTensor = new DenseTensor<long>(new[] { 1, inputIds.Length });

            for (int i = 0; i < inputIds.Length; i++)
            {
                idsTensor[0, i] = inputIds[i];
                maskTensor[0, i] = attentionMask[i];
                typeTensor[0, i] = 0;
            }

            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("input_ids", idsTensor),
                NamedOnnxValue.CreateFromTensor("attention_mask", maskTensor),
                NamedOnnxValue.CreateFromTensor("token_type_ids", typeTensor)
            };

            using var results = _session.Run(inputs);
            var output = results.First().AsTensor<float>();

            // Mean pooling over attended tokens
            var embedding = new float[_config.Dimensions];
            var attendedCount = attentionMask.Count(m => m == 1);

            for (int d = 0; d < _config.Dimensions; d++)
            {
                float sum = 0;
                for (int t = 0; t < inputIds.Length; t++)
                {
                    if (attentionMask[t] == 1)
                        sum += output[0, t, d];
                }
                embedding[d] = sum / attendedCount;
            }

            // L2 normalize
            var norm = MathF.Sqrt(embedding.Sum(x => x * x));
            if (norm > 0)
                for (int i = 0; i < embedding.Length; i++)
                    embedding[i] /= norm;

            return embedding;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate embedding for text of length {Length}", text.Length);
            return null;
        }
    }

    private (long[] InputIds, long[] AttentionMask) Tokenize(string text, int maxLength = 512)
    {
        if (_tokenizer != null)
        {
            // BertTokenizer.EncodeToIds with addSpecialTokens adds [CLS] and [SEP] automatically
            var ids = _tokenizer.EncodeToIds(text, addSpecialTokens: true);
            var truncated = ids.Count > maxLength ? ids.Take(maxLength).ToList() : ids;
            var longIds = truncated.Select(id => (long)id).ToArray();
            var mask = Enumerable.Repeat(1L, longIds.Length).ToArray();
            return (longIds, mask);
        }

        return FallbackTokenize(text, maxLength);
    }

    private static (long[] InputIds, long[] AttentionMask) FallbackTokenize(string text, int maxLength)
    {
        var ids = new List<long> { 101 }; // [CLS]

        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (ids.Count >= maxLength - 1) break;
            var hash = (long)(Math.Abs(word.ToLowerInvariant().GetHashCode()) % 29998 + 2);
            ids.Add(hash);
        }

        ids.Add(102); // [SEP]

        var mask = Enumerable.Repeat(1L, ids.Count).ToArray();
        return (ids.ToArray(), mask);
    }

    /// <summary>
    /// Resolve model file path: check configured path first, then bundled path next to the assembly.
    /// </summary>
    private static string? ResolveModelPath(string configuredPath, string fileName)
    {
        if (File.Exists(configuredPath))
            return configuredPath;

        // Check next to the running assembly (bundled models)
        var assemblyDir = Path.GetDirectoryName(typeof(OnnxEmbeddingService).Assembly.Location);
        if (assemblyDir != null)
        {
            // Check in Embeddings/Models/ subfolder (matches project structure)
            var bundledPath = Path.Combine(assemblyDir, "Embeddings", "Models", fileName);
            if (File.Exists(bundledPath))
                return bundledPath;

            // Check directly in output dir
            var directPath = Path.Combine(assemblyDir, fileName);
            if (File.Exists(directPath))
                return directPath;
        }

        return null;
    }

    public static float CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length) return 0;

        float dot = 0, normA = 0, normB = 0;
        for (int i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        var denom = MathF.Sqrt(normA) * MathF.Sqrt(normB);
        return denom > 0 ? dot / denom : 0;
    }

    public void Dispose()
    {
        _session?.Dispose();
    }
}
