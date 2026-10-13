import { createContext, useContext } from 'react';

const AccessContext = createContext(null);

export function AccessProvider({ children, access }) {
  return (
    <AccessContext.Provider value={access}>
      {children}
    </AccessContext.Provider>
  );
}

export function useAccess() {
  const context = useContext(AccessContext);

  if (!context) {
    throw new Error('useAccess must be used inside AccessProvider');
  }

  return context;
}