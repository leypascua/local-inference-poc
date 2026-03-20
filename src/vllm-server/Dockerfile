FROM vllm/vllm-openai:nightly

RUN pip install --no-cache-dir --upgrade --no-deps "https://github.com/huggingface/transformers/archive/refs/heads/main.zip" \
  && pip install --no-cache-dir --upgrade --no-deps "https://github.com/huggingface/huggingface_hub/archive/refs/heads/main.zip"
