import React, { useEffect, useState } from 'react';
import StackTrace from 'stacktrace-js';
import translate from 'Utilities/String/translate';
import styles from './ErrorBoundaryError.css';

interface ErrorBoundaryErrorProps {
  className: string;
  messageClassName: string;
  detailsClassName: string;
  message: string;
  error: Error;
  info: {
    componentStack: string;
  };
}

function ErrorBoundaryError(props: ErrorBoundaryErrorProps) {
  const {
    className = styles.container,
    messageClassName = styles.message,
    detailsClassName = styles.details,
    message = translate('ErrorLoadingContent'),
    error,
    info,
  } = props;

  const [detailedError, setDetailedError] = useState<
    StackTrace.StackFrame[] | null
  >(null);

  useEffect(() => {
    if (error) {
      StackTrace.fromError(error).then((de) => {
        setDetailedError(de);
      });
    } else {
      setDetailedError(null);
    }
  }, [error, setDetailedError]);

  return (
    <div className={className}>
      <div className={messageClassName}>{message}</div>

      {/* the crash card: a kaomoji in the brand purple instead of Readarr's monster
          (the maintainer, 2026-09-19); the stack trace stays below in <details> */}
      <div className={styles.imageContainer}>
        <div className={styles.card}>
          <div className={styles.kaomoji}>(x_x)</div>
          <div className={styles.caption}>
            {translate('ErrorBoundaryCaption')}
          </div>
        </div>
      </div>

      <details className={detailsClassName}>
        {error ? <div>{error.message}</div> : null}

        {detailedError ? (
          detailedError.map((d, index) => {
            return (
              <div key={index}>
                {`  at ${d.functionName} (${d.fileName}:${d.lineNumber}:${d.columnNumber})`}
              </div>
            );
          })
        ) : (
          <div>{info.componentStack}</div>
        )}

        {
          <div className={styles.version}>
            {translate('VersionNumber', { version: window.Readarr.version })}
          </div>
        }
      </details>
    </div>
  );
}

export default ErrorBoundaryError;
