import { useEffect, useState } from 'react';
import { getFileLink } from '../api/files';

// <SecureFileImage fileId="..." alt="Aadhaar" className="img-fluid" />
export default function SecureFileImage({ fileId, alt = '', className, style }) {
  const [url, setUrl] = useState(null);
  const [error, setError] = useState(false);

  useEffect(() => {
    let cancelled = false;
    let timer;

    async function load() {
      try {
        const data = await getFileLink(fileId);
        if (cancelled) return;
        setUrl(data.url);
        setError(false);
        // refresh 30s before the signed link expires
        const ms = Math.max(5000, data.expiresAt * 1000 - Date.now() - 30000);
        timer = setTimeout(load, ms);
      } catch {
        if (!cancelled) setError(true);
      }
    }

    load();
    return () => { cancelled = true; clearTimeout(timer); };
  }, [fileId]);

  if (error) return <span className="text-danger small">Unable to load file</span>;
  if (!url) return <span className="spinner-border spinner-border-sm" role="status" />;
  return <img src={url} alt={alt} className={className} style={style} />;
}