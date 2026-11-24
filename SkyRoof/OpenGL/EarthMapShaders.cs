namespace SkyRoof
{
  internal static class EarthMapShaders
  {
    internal const string Vertex = """
      #version 330 core
      out vec2 ndc;
      void main()
      {
        vec2 p = vec2(float((gl_VertexID << 1) & 2), float(gl_VertexID & 2));
        ndc = p * 2.0 - 1.0;
        gl_Position = vec4(ndc, 0.0, 1.0);
      }
      """;

    internal const string Fragment = """
      #version 330 core
      const float PI = 3.141592653589793;
      const float TAU = 6.283185307179586;
      uniform sampler2D worldMapTexture;
      uniform sampler2DArray tileTexture;
      uniform isampler2D tileIndex;
      uniform int level, originX, originY;
      uniform float originU, originV;
      uniform float centerLat, centerLon, satLat, satLon, footprint, zoom, aspect, brightness;
      in vec2 ndc;
      out vec4 out_Color;

      vec3 geo(float lat, float lon)
      {
        return vec3(cos(lat)*cos(lon), cos(lat)*sin(lon), sin(lat));
      }

      // Integer logical tile coordinates avoid float precision loss when
      // the provider's global matrix contains hundreds of thousands of columns.
      int findSlot(ivec2 tile, int z, bool labels)
      {
        int tag = z*2 + (labels ? 1 : 0);
        uint hash = uint(tile.x)*73856093u ^ uint(tile.y)*19349663u ^ uint(tag)*83492791u;
        int index = int(hash & 2047u);
        for (int i = 0; i < 2048; ++i)
        {
          ivec4 entry = texelFetch(tileIndex, ivec2(index, 0), 0);
          if (entry.w == 0) return -1;
          if (entry.x == tile.x && entry.y == tile.y && entry.z == tag)
            return entry.w - 1;
          index = (index + 1) & 2047;
        }
        return -1;
      }

      vec4 sampleTiles(ivec2 tile, vec2 uv, vec4 fallback)
      {
        for (int z = level; z >= 1; --z)
        {
          int factor = 1 << (level-z);
          ivec2 parent = tile / factor;
          vec2 local = (vec2(tile - parent*factor) + uv)/float(factor);
          int slot = findSlot(parent, z, false);
          if (slot < 0) continue;
          vec4 base = texture(tileTexture, vec3(local, float(slot)));
          int annotation = findSlot(parent, z, true);
          if (annotation >= 0)
          {
            vec4 label = texture(tileTexture, vec3(local, float(annotation)));
            base.rgb = mix(base.rgb, label.rgb, label.a);
          }
          return vec4(mix(fallback.rgb, base.rgb, base.a), 1.0);
        }
        return fallback;
      }

      // Spherical exponential map, shared with the CPU camera. For a
      // small viewport radius, use the local series to preserve deltas
      // below the precision of absolute float latitude/longitude.
      vec2 geoDelta(vec2 tangent, float r, float lat, float lon)
      {
        float c = cos(centerLat);
        if (r < 0.0005 && abs(c) > 0.1)
        {
          float e = tangent.x*r, n = tangent.y*r;
          float t = tan(centerLat);
          return vec2((e + e*n*t)/c, n - 0.5*t*e*e);
        }
        float dl = lon - centerLon;
        if (dl > PI) dl -= TAU;
        if (dl < -PI) dl += TAU;
        return vec2(dl, lat-centerLat);
      }

      void main()
      {
        vec2 p = ndc * vec2(max(aspect,1.0), max(1.0/aspect,1.0)) / zoom;
        float r = length(p)*PI;
        if (r > PI) discard;
        vec2 tangent = r > 0.0 ? normalize(p) : vec2(0.0,0.0);
        vec3 center = geo(centerLat, centerLon);
        vec3 east = vec3(-sin(centerLon), cos(centerLon), 0.0);
        vec3 north = vec3(-sin(centerLat)*cos(centerLon),
          -sin(centerLat)*sin(centerLon), cos(centerLat));
        vec3 point = center*cos(r) + (east*tangent.x + north*tangent.y)*sin(r);
        float lat = atan(point.z, length(point.xy));
        float lon = atan(point.y, point.x);
        vec4 fallback = texture(worldMapTexture, vec2(0.5+lon/TAU, 0.5-lat/PI));

        int width = 1 << level;
        int height = width / 2;
        vec2 delta = geoDelta(tangent, r, lat, lon);
        vec2 offset = vec2(originU,originV) + delta*vec2(float(width)/TAU,-float(height)/PI);
        ivec2 tile = ivec2(originX,originY) + ivec2(floor(offset));
        vec2 uv = fract(offset);
        tile.x = (tile.x % width + width) % width;
        if (tile.y < 0) { tile.y = 0; uv.y = 0.0; }
        if (tile.y >= height) { tile.y = height-1; uv.y = 1.0; }
        out_Color = sampleTiles(tile, uv, fallback);
        out_Color.rgb *= brightness;
        if (footprint < PI && dot(point,geo(satLat,satLon)) < cos(footprint))
          out_Color *= 0.8;
      }
      """;
  }
}
