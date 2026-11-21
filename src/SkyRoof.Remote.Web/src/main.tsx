import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import App from './App';
import './style.css';
import 'dockview-react/dist/styles/dockview.css';
const root = document.getElementById('root');
if (!root) throw new Error('App root unavailable');
createRoot(root).render(<StrictMode><App /></StrictMode>);
