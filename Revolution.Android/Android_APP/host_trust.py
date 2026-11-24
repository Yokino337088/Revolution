"""在电脑当前用户的私有目录保存证书及手机授权，运行中的令牌另由 connect.py 签发。"""
from contextlib import closing, contextmanager
from datetime import datetime, timedelta, timezone
import hashlib
import os
from pathlib import Path
import secrets
import sqlite3


def default_directory() -> Path:
    """按操作系统选当前用户的持久状态目录，仓库或插件升级后继续使用。"""
    if os.name == "nt":
        root = Path(os.environ.get("LOCALAPPDATA") or Path.home() / "AppData/Local")
    else:
        root = Path(os.environ.get("XDG_STATE_HOME") or Path.home() / ".local/state")
    return root / "Revolution" / "AndroidCompanion"


class HostTrust:
    """保存电脑身份和手机授权摘要，支持同一用户启动不同端口及撤销历史设备。"""

    def __init__(self, directory: Path):
        """创建私有状态目录并升级空数据库，已有身份和设备原样读取。"""
        directory = directory.expanduser().resolve()
        directory.mkdir(mode=0o700, parents=True, exist_ok=True)
        # 数据库只供本电脑当前用户使用，所有进程以短事务处理相同身份。
        self.path = directory / "trust.sqlite3"
        with self.database() as db:
            db.execute("CREATE TABLE IF NOT EXISTS identity (slot INTEGER PRIMARY KEY, pem BLOB NOT NULL)")
            db.execute("CREATE TABLE IF NOT EXISTS devices (digest BLOB PRIMARY KEY)")

    @contextmanager
    def database(self):
        """执行短事务，退出时提交或回滚，并释放 SQLite 文件句柄。"""
        with closing(sqlite3.connect(self.path, timeout=10)) as db, db:
            yield db

    def identity(self) -> bytes:
        """首次创建电脑证书，后续启动复用同一份 PEM。写锁防止两个监听器产生不同身份。"""
        with self.database() as db:
            db.execute("BEGIN IMMEDIATE")
            saved = db.execute("SELECT pem FROM identity WHERE slot = 1").fetchone()
            if saved:
                return saved[0]
            pem = new_identity()
            db.execute("INSERT INTO identity VALUES (1, ?)", (pem,))
            return pem

    def remember(self) -> str:
        """为成功完成一次配对的手机发放高熵凭据，只保存 SHA-256 摘要。"""
        token = secrets.token_hex(32)
        with self.database() as db:
            db.execute("INSERT INTO devices VALUES (?)", (hashlib.sha256(token.encode("ascii")).digest(),))
        return token

    def recognizes(self, token: bytes) -> bool:
        """只认可库中仍有效的长期凭据，并让接线器继续签发新的会话令牌。"""
        with self.database() as db:
            return db.execute("SELECT 1 FROM devices WHERE digest = ?",
                              (hashlib.sha256(token).digest(),)).fetchone() is not None

    def revoke(self) -> int:
        """撤销已登记手机的重连授权，保留电脑证书和指纹，返回撤销数量。"""
        with self.database() as db:
            return db.execute("DELETE FROM devices").rowcount


def new_identity() -> bytes:
    """签发只用于指纹匹配的长期电脑证书，IP 和端口由每次启动的监听设置确定。"""
    from cryptography import x509
    from cryptography.hazmat.primitives import hashes, serialization
    from cryptography.hazmat.primitives.asymmetric import ec
    from cryptography.x509.oid import NameOID

    key = ec.generate_private_key(ec.SECP256R1())
    name = x509.Name([x509.NameAttribute(NameOID.COMMON_NAME, "Revolution Android test host")])
    now = datetime.now(timezone.utc)
    cert = (x509.CertificateBuilder().subject_name(name).issuer_name(name).public_key(key.public_key())
            .serial_number(x509.random_serial_number()).not_valid_before(now - timedelta(days=1))
            .not_valid_after(now + timedelta(days=3650)).add_extension(
                x509.BasicConstraints(ca=False, path_length=None), critical=True).sign(key, hashes.SHA256()))
    return cert.public_bytes(serialization.Encoding.PEM) + key.private_bytes(
        serialization.Encoding.PEM, serialization.PrivateFormat.PKCS8, serialization.NoEncryption())
