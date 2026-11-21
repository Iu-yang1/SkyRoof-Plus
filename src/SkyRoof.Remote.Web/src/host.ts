import { createContext } from 'react';
import { HubConnectionBuilder, type HubConnection } from '@microsoft/signalr';
import { MessagePackHubProtocol } from '@microsoft/signalr-protocol-msgpack';

export const DeviceHubContext = createContext<HubConnection | null>(null);

export enum DeviceOperation {
  RxFrequency = 1,
  TxFrequency = 2,
  RxMode = 3,
  TxMode = 4,
  ManualBegin = 5,
  ManualEnd = 6,
  DigitalBegin = 7,
  DigitalEnd = 8,
  Stop = 9,
  ResumeDigital = 10
}

export interface DeviceReadback {
  RxFrequencyHz: number;
  TxFrequencyHz: number;
  RxMode: string;
  TxMode: string;
  ActualPtt: boolean;
  ActiveTransactionId: string;
  ActiveSessionId: string;
  Epoch: number;
  Revision: number;
  DigitalAutomationPaused: boolean;
  Outcome: string;
}

export interface StationInfo { protocol: string; runtimeMode: string; user: string | null }

export async function queryStation(): Promise<StationInfo | null> {
  const response = await fetch('/api/v1/station', { credentials: 'include' });
  if (response.status === 401) return null;
  if (!response.ok) throw new Error('Station API HTTP ' + response.status);
  return response.json() as Promise<StationInfo>;
}

export async function signIn(userName: string, password: string): Promise<void> {
  const response = await fetch('/api/v1/auth/login', {
    method: 'POST', credentials: 'include',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ userName, password })
  });
  if (!response.ok) throw new Error('Login HTTP ' + response.status);
}

export async function connectControl(): Promise<HubConnection> {
  const hub = new HubConnectionBuilder()
    .withUrl('/hub/control', { withCredentials: true })
    .withHubProtocol(new MessagePackHubProtocol())
    .withAutomaticReconnect([0, 1000, 3000, 10000])
    .build();
  await hub.start();
  return hub;
}
