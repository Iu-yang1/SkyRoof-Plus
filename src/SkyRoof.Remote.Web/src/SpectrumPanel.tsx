import { useEffect, useRef, useState } from 'react';

const bins = 475;
const vs = '#version 300 es\nin vec2 aPoint; void main() { gl_Position=vec4(aPoint,0.0,1.0); }';
const fs = '#version 300 es\nprecision mediump float; out vec4 pixel; void main(){pixel=vec4(0.35,0.76,0.98,1.0);}';

function compile(gl: WebGL2RenderingContext, type: number, source: string): WebGLShader {
  const shader = gl.createShader(type);
  if (!shader) throw new Error('WebGL2 shader allocation');
  gl.shaderSource(shader, source);
  gl.compileShader(shader);
  if (!gl.getShaderParameter(shader, gl.COMPILE_STATUS))
    throw new Error(gl.getShaderInfoLog(shader) ?? 'WebGL2 shader failure');
  return shader;
}

export function SpectrumPanel({ audioFrequency = false }: { audioFrequency?: boolean } = {}) {
  const canvas = useRef<HTMLCanvasElement>(null);
  const [running, setRunning] = useState(false);
  const [status, setStatus] = useState('No source');
  const [count, setCount] = useState(0);
  useEffect(() => {
    if (!running || !canvas.current) return;
    const node = canvas.current;
    const gl = node.getContext('webgl2');
    if (!gl) { setStatus('WebGL2 unsupported'); return; }
    const vertex = compile(gl, gl.VERTEX_SHADER, vs);
    const fragment = compile(gl, gl.FRAGMENT_SHADER, fs);
    const program = gl.createProgram();
    if (!program) { setStatus('Program unavailable'); return; }
    gl.attachShader(program, vertex);
    gl.attachShader(program, fragment);
    gl.linkProgram(program);
    const buffer = gl.createBuffer();
    const loc = gl.getAttribLocation(program, 'aPoint');
    let latest = -1;
    let epoch = -1;
    let received = 0;
    const proto = location.protocol === 'https:' ? 'wss:' : 'ws:';
    const ws = new WebSocket(proto + '//' + location.host + (audioFrequency ? '/ws/af-spectrum' : '/sim/ws/spectrum'));
    ws.binaryType = 'arraybuffer';
    ws.onopen = () => setStatus(audioFrequency ? 'LIVE AF · 0–24 kHz' : 'SIMULATED spectrum');
    ws.onclose = () => setStatus('Disconnected');
    ws.onerror = () => setStatus('WSS unavailable');
    ws.onmessage = (ev: MessageEvent<ArrayBuffer>) => {
      const data = new DataView(ev.data);
      if (data.byteLength !== 24 + bins * 4 || data.getUint32(0, true) !== 0x31505353 ||
          data.getInt32(16, true) !== bins) return;
      if (audioFrequency) {
        const currentEpoch = data.getUint32(20, true);
        if (epoch !== currentEpoch) { latest = -1; epoch = currentEpoch; }
      }
      const sequence = Number(data.getBigInt64(8, true));
      if (sequence <= latest) return;
      latest = sequence;
      const vertices = new Float32Array(bins * 2);
      for (let i = 0; i < bins; i++) {
        const db = data.getFloat32(24 + i * 4, true);
        vertices[2 * i] = 2 * i / (bins - 1) - 1;
        vertices[2 * i + 1] = Math.max(-1, Math.min(1, (db + 90) / 50));
      }
      node.width = Math.max(1, Math.round(node.clientWidth * devicePixelRatio));
      node.height = Math.max(1, Math.round(node.clientHeight * devicePixelRatio));
      gl.viewport(0, 0, node.width, node.height);
      gl.clearColor(.03, .07, .11, 1);
      gl.clear(gl.COLOR_BUFFER_BIT);
      gl.useProgram(program);
      gl.bindBuffer(gl.ARRAY_BUFFER, buffer);
      gl.bufferData(gl.ARRAY_BUFFER, vertices, gl.DYNAMIC_DRAW);
      gl.enableVertexAttribArray(loc);
      gl.vertexAttribPointer(loc, 2, gl.FLOAT, false, 0, 0);
      gl.drawArrays(gl.LINE_STRIP, 0, bins);
      received++;
      if (received % 15 === 0) setCount(received);
    };
    return () => {
      ws.close();
      gl.deleteBuffer(buffer);
      gl.deleteProgram(program);
      gl.deleteShader(vertex);
      gl.deleteShader(fragment);
    };
  }, [running, audioFrequency]);
  return <section className="panel scope">
    <header>{audioFrequency ? 'AF SPECTRUM · 475 BINS' : 'SIMULATED RF SPECTRUM · 475 BINS'}</header>
    <canvas ref={canvas} aria-label={audioFrequency ? "Audio frequency spectrum" : "Simulated frequency spectrum"} />
    <footer><span>{status} · {count} frames</span>
      <button onClick={() => setRunning(value => !value)}>{running ? 'STOP' : audioFrequency ? 'RUN AF' : 'SIMULATE'}</button>
    </footer>
    <small>{audioFrequency ? '实际 48 kHz PCM 音频 FFT，仅表示 0–24 kHz AF，不代表 RF/IF 频谱。' : '模拟占位频谱，不代表 IC-9700 RF/IF 数据。'}</small>
  </section>;
}
