import os
from dotenv import load_dotenv

load_dotenv()


class Settings:
    llm_base_url: str = os.getenv("LLM_BASE_URL", "https://api.openai.com/v1")
    llm_api_key: str = os.getenv("LLM_API_KEY", "")
    llm_model: str = os.getenv("LLM_MODEL", "gpt-4o-mini")
    llm_timeout: int = int(os.getenv("LLM_TIMEOUT", "20"))

    embedding_base_url: str = os.getenv("EMBEDDING_BASE_URL", os.getenv("LLM_BASE_URL", ""))
    embedding_api_key: str = os.getenv("EMBEDDING_API_KEY", os.getenv("LLM_API_KEY", ""))
    embedding_model: str = os.getenv("EMBEDDING_MODEL", "text-embedding-3-small")
    embedding_dim: int = int(os.getenv("EMBEDDING_DIM", "1536"))

    rules_only: bool = os.getenv("RULES_ONLY", "false").lower() == "true"
    timezone: str = os.getenv("TIMEZONE", "Europe/Moscow")


settings = Settings()
