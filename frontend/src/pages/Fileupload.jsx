import { useState } from 'react';
import FileManager from '../components/FileManager';

export default function FileUploadTest() {
  const [files, setFiles] = useState([]);

  return (
    <div className="container py-4" style={{ maxWidth: 760 }}>
      <h5 className="mb-3">File module test</h5>

      <div className="card mb-3">
        <div className="card-body">
          <FileManager
            category="test-docs"
            allowTypeChoice
            accept=".pdf,.jpg,.jpeg,.png"
            onChange={setFiles}
          />
          
        </div>
      </div>

      <div className="card">
        <div className="card-header small">What the page would store (fileId + names)</div>
        <pre className="card-body mb-0 small">
          {JSON.stringify(
            files.map(({ fileId, storedFileName, originalFileName, type, category }) => ({ fileId, storedFileName, originalFileName, type, category })),
            null,
            2
          )}
        </pre>
      </div>
    </div>
  );
}