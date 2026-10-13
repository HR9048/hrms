import { useCallback, useEffect, useState } from 'react';
import { getOrgScope } from '../api/dataScope';

// Not cached on purpose: switching accounts (AccountSwitcher) changes the user's scope.
export default function useOrgScope() {
  const [state, setState] = useState({ loading: true, data: null, error: null });

  const load = useCallback(() => {
    setState((s) => ({ ...s, loading: true }));
    return getOrgScope()
      .then((data) => setState({ loading: false, data, error: null }))
      .catch((error) => setState({ loading: false, data: null, error }));
  }, []);

  useEffect(() => { load(); }, [load]);

  return { ...state, reload: load }; // data = { scope, organizations, nodeTypes, nodes }
}