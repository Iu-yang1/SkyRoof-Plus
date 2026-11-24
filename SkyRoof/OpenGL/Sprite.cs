using System;
using System.Collections.Generic;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Linq;
using System.Numerics;
using System.Runtime;
using System.Security.Policy;
using System.Text;
using System.Threading.Tasks;
using SharpGL;
using SharpGL.Shaders;
using SharpGL.VertexBuffers;
using VE3NEA;

namespace SkyRoof
{
  public class Sprite
  {
    private OpenGL gl;
    private uint[] TextureIds = new uint[1];
    private Size ImageSize;
    private float scale = 1;

    public bool Enabled = true;
    public PointF Location;
    public float Angle;
    public float Scale { get => scale; set => SetScale(value); }
    public uint TextureId => TextureIds[0];
    public Matrix4x4 Transform => GetTransform();


    public Sprite(OpenGL gl, byte[] imageBytes)
    {
      this.gl = gl;
      LoadTexture(imageBytes);
    }

    private void LoadTexture(byte[] imageBytes)
    {
      gl.ActiveTexture(OpenGL.GL_TEXTURE0);

      gl.GenTextures(1, TextureIds);
      gl.BindTexture(OpenGL.GL_TEXTURE_2D, TextureId);

      gl.TexParameter(OpenGL.GL_TEXTURE_2D, OpenGL.GL_TEXTURE_MIN_FILTER, OpenGL.GL_LINEAR);    // shrink
      gl.TexParameter(OpenGL.GL_TEXTURE_2D, OpenGL.GL_TEXTURE_MAG_FILTER, OpenGL.GL_LINEAR);    // stretch
      gl.TexParameter(OpenGL.GL_TEXTURE_2D, OpenGL.GL_TEXTURE_WRAP_T, OpenGL.GL_CLAMP_TO_EDGE); // tex x
      gl.TexParameter(OpenGL.GL_TEXTURE_2D, OpenGL.GL_TEXTURE_WRAP_S, OpenGL.GL_CLAMP_TO_EDGE); // tex y

      using (Bitmap bitmap = Utils.BitmapFromBytes(imageBytes))
      {
        ImageSize = bitmap.Size;

        BitmapData data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height),
        ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

        gl.TexImage2D(OpenGL.GL_TEXTURE_2D, 0, OpenGL.GL_RGBA, bitmap.Width, bitmap.Height, 0,
          OpenGL.GL_BGRA, OpenGL.GL_UNSIGNED_BYTE, data.Scan0);

        bitmap.UnlockBits(data);
      }      
    }  
    

    private void SetScale(float value) 
    { 
      scale = Math.Max(0.25f, Math.Min(1, value)); 
    }

    int[] viewport = new int[4]; // 0, 0, W, H
    private Matrix4x4 GetTransform()
    {      
      gl.GetInteger(OpenGL.GL_VIEWPORT, viewport);

      return BuildTransform(Location, Angle, Scale, ImageSize,
        new Size(viewport[2], viewport[3]));
    }

    internal static Matrix4x4 BuildTransform(PointF location, float angle,
      float scale, Size image, Size viewportSize)
    {
      // Rotate in pixel space around the sprite's own centre. Apply the
      // geographic translation last so rotating an off-centre satellite
      // never rotates its position around the centre of the Earth view.
      return Matrix4x4.CreateScale(scale * image.Width, scale * image.Height, 1) *
        Matrix4x4.CreateRotationZ(angle) *
        Matrix4x4.CreateScale(1f / viewportSize.Width, 1f / viewportSize.Height, 1) *
        Matrix4x4.CreateTranslation(location.X, location.Y, 0);
    }
  }
}
