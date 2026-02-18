import { useState, useEffect } from 'react';

interface Document {
  id: string;
  fileName: string;
  uploadedAt: string;
  fileSize: number;
}

export default function DocumentViewer() {
  const [documents, setDocuments] = useState<Document[]>([]);
  const [loading, setLoading] = useState(true);
  const [selectedDoc, setSelectedDoc] = useState<string | null>(null);

  useEffect(() => {
    // Fetch document list from backend
    fetch('/api/documents')
      .then(res => res.json())
      .then(data => {
        setDocuments(data.documents || []);
        setLoading(false);
      })
      .catch(err => {
        console.error('Failed to load documents:', err);
        setLoading(false);
      });
  }, []);

  const openDocument = (docId: string) => {
    // Open PDF in new tab or iframe
    window.open(`/api/documents/${docId}/download`, '_blank');
    setSelectedDoc(docId);
  };

  return (
    <div className="px-4">
      <h1 className="text-3xl font-bold text-gray-900 mb-8">Documents</h1>

      {loading ? (
        <p className="text-gray-600">Loading documents...</p>
      ) : documents.length === 0 ? (
        <div className="bg-white rounded-lg shadow p-6">
          <p className="text-gray-600">No documents uploaded yet.</p>
        </div>
      ) : (
        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-6">
          {documents.map((doc) => (
            <div
              key={doc.id}
              onClick={() => openDocument(doc.id)}
              className={`bg-white rounded-lg shadow p-6 cursor-pointer hover:shadow-md transition-shadow ${
                selectedDoc === doc.id ? 'ring-2 ring-blue-500' : ''
              }`}
            >
              <div className="text-4xl mb-3">📄</div>
              <h3 className="font-semibold text-gray-900 mb-2 truncate">{doc.fileName}</h3>
              <p className="text-sm text-gray-600">
                {new Date(doc.uploadedAt).toLocaleDateString()}
              </p>
              <p className="text-sm text-gray-500">
                {(doc.fileSize / 1024).toFixed(1)} KB
              </p>
            </div>
          ))}
        </div>
      )}
    </div>
  );
}
