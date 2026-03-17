#!/usr/bin/env bash
# Downloads the Granite 4.0 1B Speech ONNX model (quantized variant for CPU inference).
# Usage: ./scripts/download-granite-model.sh
set -euo pipefail

MODEL_DIR="lucia.AgentHost/models/stt/granite-4.0-1b-speech"
HF_BASE="https://huggingface.co/onnx-community/granite-4.0-1b-speech-ONNX/resolve/main"

mkdir -p "$MODEL_DIR/onnx"

echo "Downloading Granite 4.0 1B Speech ONNX model (quantized)..."

# Config & tokenizer (small files)
for f in config.json generation_config.json preprocessor_config.json processor_config.json tokenizer.json tokenizer_config.json; do
    echo "  $f"
    curl -sSL "$HF_BASE/$f" -o "$MODEL_DIR/$f"
done

# Quantized ONNX models (int8 — good accuracy/speed balance)
for f in audio_encoder_quantized.onnx audio_encoder_quantized.onnx_data \
         embed_tokens_quantized.onnx embed_tokens_quantized.onnx_data \
         decoder_model_merged_quantized.onnx decoder_model_merged_quantized.onnx_data; do
    if [ -f "$MODEL_DIR/onnx/$f" ]; then
        echo "  onnx/$f (already exists, skipping)"
    else
        echo "  onnx/$f"
        curl -SL "$HF_BASE/onnx/$f" -o "$MODEL_DIR/onnx/$f"
    fi
done

echo ""
echo "Done! Model downloaded to $MODEL_DIR"
echo "Run the validation test:"
echo "  dotnet test lucia.Tests/lucia.Tests.csproj --filter EnhancementPipeline_WavSample_ProducesTranscript -v normal"
