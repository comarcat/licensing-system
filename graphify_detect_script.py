import json
import sys
from pathlib import Path
from graphify.detect import detect

# Set input path
input_path = Path("C:/Users/miauadmin/OneDrive/Documentos/GitHub/licensing-system")

# Run detection
result = detect(input_path)

# Ensure output directory exists
Path("graphify-out").mkdir(exist_ok=True)

# Write sidecar
Path("graphify-out/.graphify_detect.json").write_text(json.dumps(result, ensure_ascii=False), encoding="utf-8")
print(f'Detected {result.get("total_files", 0)} files')
