import { Outlet } from 'react-router-dom';
import Navbar from './Navbar.jsx';

export default function Layout() {
  return (
    <Navbar>
      <Outlet />
    </Navbar>
  );
}