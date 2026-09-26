# Optional local voice experiments

This folder pins a Python environment and includes sample voice takes. The current camera, relay, and Unity demo works without these models. A complete voice coach or evidence-agent service has not been integrated.

Use Python 3.12 and `uv` on Apple Silicon (the environment includes MLX):

```bash
cd coach
uv sync --frozen
cd ..
python3 scripts/fetch_voice_models.py
```

The files come from the upstream [Kokoro ONNX model-files-v1.0 release](https://github.com/thewh1teagle/kokoro-onnx/releases/tag/model-files-v1.0):

| File | SHA-256 |
| --- | --- |
| `kokoro-v1.0.onnx` | `7d5df8ecf7d4b1878015a32686053fd0eebe2bc377234608764cc0ef3636a6c5` |
| `voices-v1.0.bin` | `bca610b8308e8d99f32e6fe4197e7ec01679264efed0cac9140fe9c29f1fbf7d` |

Weights are stored under ignored `coach/models/`; the roughly 310 MiB ONNX file is downloaded instead of committed. Existing matching files are reused. Failed downloads or checksum mismatches preserve the previous file.

Verify existing files without network access:

```bash
python3 scripts/fetch_voice_models.py --check
```
