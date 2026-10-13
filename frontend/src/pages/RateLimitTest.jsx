import { useRef, useState } from "react";

const API_URL = "http://localhost:5000/api/health";

export default function RateLimitTest() {
  const [results, setResults] = useState([]);
  const [running, setRunning] = useState(false);
  const [total, setTotal] = useState(0);
  const [success, setSuccess] = useState(0);
  const [blocked, setBlocked] = useState(0);

  const stopRef = useRef(false);

  const startTest = async () => {
    stopRef.current = false;
    setRunning(true);
    setResults([]);
    setTotal(0);
    setSuccess(0);
    setBlocked(0);

    // Send 150 requests
    for (let i = 1; i <= 1500; i++) {
      if (stopRef.current) break;

      const start = performance.now();

      try {
        const response = await fetch(API_URL, {
          method: "GET",
        });

        const end = performance.now();

        let body;

        try {
          body = await response.json();
        } catch {
          body = {};
        }

        const result = {
          request: i,
          status: response.status,
          statusText: response.statusText,
          time: Math.round(end - start),
          body,
        };

        setResults((prev) => [result, ...prev].slice(0, 200));
        setTotal(i);

        if (response.status === 429) {
          setBlocked((prev) => prev + 1);
        } else if (response.ok) {
          setSuccess((prev) => prev + 1);
        }
      } catch (error) {
        setResults((prev) => [
          {
            request: i,
            status: "ERROR",
            statusText: error.message,
            time: 0,
            body: {},
          },
          ...prev,
        ]);

        setTotal(i);
      }
    }

    setRunning(false);
  };

  const stopTest = () => {
    stopRef.current = true;
    setRunning(false);
  };

  const clearResults = () => {
    setResults([]);
    setTotal(0);
    setSuccess(0);
    setBlocked(0);
  };

  return (
    <div className="container-fluid p-4">

      <div className="card shadow-sm">

        <div className="card-header">
          <h4 className="mb-1">API Rate Limit Test</h4>
          <small className="text-muted">
            Testing: {API_URL}
          </small>
        </div>

        <div className="card-body">

          {/* Configuration */}
          <div className="alert alert-info">
            <strong>Configured Rate Limit:</strong>{" "}
            5 requests / minute / IP
          </div>

          {/* Buttons */}
          <div className="mb-4">
            {!running ? (
              <button
                className="btn btn-primary me-2"
                onClick={startTest}
              >
                Start Test
              </button>
            ) : (
              <button
                className="btn btn-danger me-2"
                onClick={stopTest}
              >
                Stop Test
              </button>
            )}

            <button
              className="btn btn-secondary"
              onClick={clearResults}
              disabled={running}
            >
              Clear
            </button>
          </div>

          {/* Statistics */}
          <div className="row g-3 mb-4">

            <div className="col-md-3">
              <div className="card border">
                <div className="card-body">
                  <small className="text-muted">
                    Total Requests
                  </small>
                  <h3>{total}</h3>
                </div>
              </div>
            </div>

            <div className="col-md-3">
              <div className="card border">
                <div className="card-body">
                  <small className="text-muted">
                    Successful
                  </small>
                  <h3 className="text-success">
                    {success}
                  </h3>
                </div>
              </div>
            </div>

            <div className="col-md-3">
              <div className="card border">
                <div className="card-body">
                  <small className="text-muted">
                    Rate Limited (429)
                  </small>
                  <h3 className="text-danger">
                    {blocked}
                  </h3>
                </div>
              </div>
            </div>

            <div className="col-md-3">
              <div className="card border">
                <div className="card-body">
                  <small className="text-muted">
                    Limit
                  </small>
                  <h3>5/min</h3>
                </div>
              </div>
            </div>

          </div>

          {/* Result message */}
          {blocked > 0 && (
            <div className="alert alert-danger">
              <strong>Rate limiting is working!</strong>
              <br />
              The API returned HTTP 429 after the request limit
              was reached.
            </div>
          )}

          {/* Request table */}
          <div
            className="table-responsive"
            style={{ maxHeight: "500px" }}
          >
            <table className="table table-bordered table-hover align-middle">

              <thead className="table-light sticky-top">
                <tr>
                  <th>#</th>
                  <th>Status</th>
                  <th>Status Text</th>
                  <th>Response Time</th>
                  <th>Response</th>
                </tr>
              </thead>

              <tbody>

                {results.length === 0 ? (
                  <tr>
                    <td
                      colSpan="5"
                      className="text-center text-muted py-4"
                    >
                      No requests sent yet.
                    </td>
                  </tr>
                ) : (
                  results.map((item, index) => (
                    <tr key={index}>

                      <td>
                        {item.request}
                      </td>

                      <td>
                        {item.status === 429 ? (
                          <span className="badge bg-danger">
                            429
                          </span>
                        ) : item.status >= 200 &&
                          item.status < 300 ? (
                          <span className="badge bg-success">
                            {item.status}
                          </span>
                        ) : (
                          <span className="badge bg-warning text-dark">
                            {item.status}
                          </span>
                        )}
                      </td>

                      <td>
                        {item.statusText}
                      </td>

                      <td>
                        {item.time} ms
                      </td>

                      <td>
                        <code>
                          {JSON.stringify(item.body)}
                        </code>
                      </td>

                    </tr>
                  ))
                )}

              </tbody>

            </table>
          </div>

        </div>
      </div>

    </div>
  );
}