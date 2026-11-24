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
      uniform int level, originX, originY, projection;
      uniform float centerLat, mercatorCenterLat;
      uniform float originU, originV;
      uniform float centerSinLat, centerCosLat, centerLon, satLat, satLon, footprint, zoom, aspect, brightness;
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
        for (int z = level; z >= (projection == 0 ? 1 : 0); --z)
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

      // Evaluate the inverse spherical projection in a longitude-relative
      // basis. Rationalize the horizontal difference to preserve small
      // latitude deltas without subtracting nearly equal absolute angles.
      vec2 geoDelta(vec2 tangent, float r, out vec3 point)
      {
        float c = centerCosLat;
        float s = centerSinLat;
        float halfSin = sin(0.5*r);
        float cosMinusOne = -2.0*halfSin*halfSin;
        float n = tangent.y*sin(r);
        float y = tangent.x*sin(r);
        float dx = c*cosMinusOne-s*n;
        float dz = s*cosMinusOne+c*n;
        float x = c+dx;
        float z = s+dz;
        float horizontal = length(vec2(x,y));
        float hsum = horizontal+c;
        float dh = hsum > 0.0 ? (dx*(x+c)+y*y)/hsum : 0.0;
        float dlat = atan(dz*c-dh*s, z*s+horizontal*c);
        float dlon = horizontal > 0.0 ? atan(y,x) : 0.0;
        float cl = cos(centerLon), sl = sin(centerLon);
        point = vec3(x*cl-y*sl, x*sl+y*cl, z);
        return vec2(dlon,dlat);
      }

      // Conventional GCJ-02 forward offset, matching the CPU tile planner.
      // Evaluate offsets relative to the CPU's double precision tile origin.
      vec2 gcjOffset(vec2 degreesLonLat)
      {
        float lon = degreesLonLat.x, lat = degreesLonLat.y;
        if (lon < 72.004 || lon > 137.8347 || lat < 0.8293 || lat > 55.8271)
          return vec2(0.0);
        float x = lon - 105.0, y = lat - 35.0;
        float common = (20.0*sin(6.0*x*PI)+20.0*sin(2.0*x*PI))*2.0/3.0;
        float dy = -100.0+2.0*x+3.0*y+0.2*y*y+0.1*x*y+0.2*sqrt(abs(x))+common;
        dy += (20.0*sin(y*PI)+40.0*sin(y*PI/3.0))*2.0/3.0;
        dy += (160.0*sin(y*PI/12.0)+320.0*sin(y*PI/30.0))*2.0/3.0;
        float dx = 300.0+x+2.0*y+0.1*x*x+0.1*x*y+0.1*sqrt(abs(x))+common;
        dx += (20.0*sin(x*PI)+40.0*sin(x*PI/3.0))*2.0/3.0;
        dx += (150.0*sin(x*PI/12.0)+300.0*sin(x*PI/30.0))*2.0/3.0;
        float phi = radians(lat), s = sin(phi);
        float ee = 0.006693421622965943, a = 6378245.0;
        float magic = 1.0-ee*s*s, root = sqrt(magic);
        return vec2(dx/(a/root*cos(phi)),dy/(a*(1.0-ee)/(magic*root)));
      }

      float mercatorDelta(float phi, float dphi)
      {
        // atanh(sin(phi+dphi))-atanh(sin(phi)), without losing
        // small angular deltas to subtraction at deep zoom.
        float ds = 2.0*cos(phi+0.5*dphi)*sin(0.5*dphi);
        float s = sin(phi);
        float t = clamp(ds/(1.0-s*(s+ds)), -0.99999994, 0.99999994);
        if (abs(t) < 0.01) return t*(1.0+t*t/3.0);
        return 0.5*log((1.0+t)/(1.0-t));
      }

      vec2 sourceOffset(vec2 delta, float lat, float lon, out bool covered)
      {
        covered = true;
        if (projection == 0) return delta*vec2(1.0/TAU,-1.0/TAU);
        vec2 offset = vec2(0.0), centerOffset = vec2(0.0);
        if (projection == 2)
        {
          offset = gcjOffset(degrees(vec2(lon,lat)));
          centerOffset = gcjOffset(degrees(vec2(centerLon,centerLat)));
        }
        float targetLat = lat+offset.y;
        float limit = radians(85.0511287798066);
        covered = abs(targetLat) <= limit;
        if (!covered) return vec2(0.0);
        float centerSourceLat = centerLat+centerOffset.y;
        float dphi = delta.y+offset.y-centerOffset.y;
        if (abs(centerSourceLat) > limit) dphi = targetLat-mercatorCenterLat;
        return vec2((delta.x+offset.x-centerOffset.x)/TAU,
          -mercatorDelta(mercatorCenterLat,dphi)/TAU);
      }

      void main()
      {
        vec2 p = ndc * vec2(max(aspect,1.0), max(1.0/aspect,1.0)) / zoom;
        float r = length(p)*PI;
        if (r > PI) discard;
        vec2 tangent = r > 0.0 ? normalize(p) : vec2(0.0,0.0);
        vec3 point;
        vec2 delta = geoDelta(tangent, r, point);
        float lat = atan(point.z, length(point.xy));
        float lon = atan(point.y, point.x);
        vec4 fallback = texture(worldMapTexture, vec2(0.5+lon/TAU, 0.5-lat/PI));

        int width = 1 << level;
        int height = projection == 0 ? width / 2 : width;
        bool covered;
        vec2 sourceDelta = sourceOffset(delta,lat,lon,covered);
        vec2 offset = vec2(originU,originV) + sourceDelta*float(width);
        ivec2 tile = ivec2(originX,originY) + ivec2(floor(offset));
        vec2 uv = fract(offset);
        tile.x = (tile.x % width + width) % width;
        if (tile.y < 0) { tile.y = 0; uv.y = 0.0; }
        if (tile.y >= height) { tile.y = height-1; uv.y = 1.0; }
        out_Color = covered ? sampleTiles(tile, uv, fallback) : fallback;
        out_Color.rgb *= brightness;
        if (footprint < PI && dot(point,geo(satLat,satLon)) < cos(footprint))
          out_Color *= 0.8;
      }
      """;
  }
}
