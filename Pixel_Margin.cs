// Name: Extend Pixel Margin
// Submenu: Object
// Author: Tumby#5171
// Title: Extend Pixel Margin
// Version: 2.0
// Desc: Fill the transparent area with nearby colors. Helps with color-bleed on game textures.
// Keywords: margin|extend|fill|alpha|voronoi
// URL:
// Help: USES RICH TEXT - DO NOT BOTHER WITH THIS
#region UICode
IntSliderControl user_alpha_threshold = 1; // [1,255,5] Alpha Threshold
CheckboxControl user_tile_x = false; // Tile Horizontally (Left/Right)
CheckboxControl user_tile_y = false; // Tile Vertically (Up/Down)
#endregion

/*******************************************************************************
    A Paint.NET plugin that fills the transparent area of an image with nearby
	colors, essentially creating a margin.
    Copyright (C) 2022  R.B. aka "Tumby" aka "Tumbolisu"

    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program.  If not, see <https://www.gnu.org/licenses/>.
	
	You can contact me via tumbolisu@gmx.de.
*******************************************************************************/

#if DEBUG
bool RENDER_DEBUG = false;
int MAX_ITERATIONS = 16;
#endif


int ModPositive(int dividend, int divisor)
{
    int result = dividend % divisor;
    if (result < 0)
    {
        result += divisor;
    }
    return result;
}


// The method used to extend the margin does not work in parallel.
// Therefore, everything is done in the PreRender.
unsafe void PreRender(Surface dst, Surface src)
{
    int w = src.Width;
    int h = src.Height;
    int size_min = (w < h) ? w : h;
    int size_max = (w > h) ? w : h;
    
    #if DEBUG
    Debug.WriteLine("Start PreRender...");
    Debug.WriteLine("Total Image Area = " + w + "x" + h + " = " + w*h);
    #endif

    Surface wrk = new Surface(src.Size);
    
    ColorBgra pix; // Pixel for temporary work, because making a pixel on the fly is cringe.

    bool[,] mask = new bool[w,h];  // Pixels that stay unchanged.
    HashSet<Point> expand = new HashSet<Point>(size_min);  // Pixels at the boarder of the mask.
    HashSet<Point> next = new HashSet<Point>(size_min);  // Same as expand, but for the next iteration.

    // Copy src to dst with alpha maxed out + Build mask.
    for (int y = 0; y < h; y++)
    {
        ColorBgra* src_ptr = src.GetPointPointerUnchecked(0, y);
        ColorBgra* dst_ptr = dst.GetPointPointerUnchecked(0, y);
        for (int x = 0; x < w; x++)
        {
            pix = *src_ptr;
            
            mask[x,y] = (pix.A >= user_alpha_threshold);
            pix.A = (byte)255;
            *dst_ptr = pix;

            src_ptr++;
            dst_ptr++;
        }
    }

    // Build initial expand-list.
    for (int py = 0; py < h; py++)
    {
        for (int px = 0; px < w; px++)
        {
            // Only low-alpha pixels can be in the expand-list.
            if (mask[px,py]) continue;
            
            // gather pixels in neighbourhood
            for (int v = -1; v <= 1; v++)
            {
                int y = py + v;

                if (y < 0)  // outside image bounds (top)
                {
                    if (user_tile_y)
                        y += h;
                    else
                        continue;
                }
                else if (y >= h)  // outside image bounds (bottom)
                {
                    if (user_tile_y)
                        y -= h;
                    else
                        continue;
                }

                for (int u = -1; u <= 1; u++)
                {
                    // a point is not a neighbour of itself
                    if (u == 0 && v == 0) continue;

                    int x = px + u;

                    if (x < 0)  // outside image bounds (left)
                    {
                        if (user_tile_x)
                            x += w;
                        else
                            continue;
                    }
                    else if (x >= w)  // outside image bounds (right)
                    {
                        if (user_tile_x)
                            x -= w;
                        else
                            continue;
                    }

                    if (mask[x,y])
                    {
                        expand.Add(new Point(px,py));
                        goto break_initial_expand_list_v;
                    }
                }  // u end
            }  // v end
            break_initial_expand_list_v:;
        }  // xx end
    }  // yy end
    
    // A list of pixels to blend together. Can be anywhere from 0 to 8 pixels.
    ColorBgra[] blend = new ColorBgra[8];
    int blend_count;
    #if DEBUG
    int HIGHEST_BLEND_COUNT = 0;
    #endif

    #if DEBUG
    for (int ITERATION = 0; ITERATION < MAX_ITERATIONS; ITERATION++)
    {
    #else
    while (expand.Count != 0)
    {
    #endif
        if (IsCancelRequested) return;

        next.Clear();

        // Color one layer of pixels and make the next-list
        foreach (Point point in expand)
        {
            blend_count = 0;  // "clear" blending list
            
            // gather pixels in neighbourhood
            for (int v = -1; v <= 1; v++)
            {
                int y = point.Y + v;

                if (y < 0)  // outside image bounds (top)
                {
                    if (user_tile_y)
                        y += h;
                    else
                        continue;
                }
                else if (y >= h)  // outside image bounds (bottom)
                {
                    if (user_tile_y)
                        y -= h;
                    else
                        continue;
                }

                for (int u = -1; u <= 1; u++)
                {
                    // a point is not a neighbour of itself
                    if (u == 0 && v == 0) continue;

                    int x = point.X + u;

                    if (x < 0)  // outside image bounds (left)
                    {
                        if (user_tile_x)
                            x += w;
                        else
                            continue;
                    }
                    else if (x >= w)  // outside image bounds (right)
                    {
                        if (user_tile_x)
                            x -= w;
                        else
                            continue;
                    }

                    // add point to next-list or to the blend-list
                    if (mask[x,y])
                    {
                        blend[blend_count] = dst[x,y];
                        blend_count += 1;
                    }
                    else
                    {
                        next.Add(new Point(x,y));
                    }
                }  // end u
            }  // end v

            if (blend_count <= 0)
            {
                continue;
            }

            pix = ColorBgra.Blend(blend, 0, blend_count);
            pix.A = (byte)255;
            
            wrk[point.X, point.Y] = pix;

            #if DEBUG
            if (HIGHEST_BLEND_COUNT < blend_count)
            {
                HIGHEST_BLEND_COUNT = blend_count;
            }
            #endif
        }

        // Repeat color changes to dst surface
        foreach (Point point in expand)
        {
            dst[point.X, point.Y] = wrk[point.X, point.Y];
        }

        // Update the mask
        foreach (Point point in expand)
        {
            mask[point.X, point.Y] = true;
        }

        // Update expand list using mask and next-list
        expand.Clear();
        foreach (Point point in next)
        {
            if (!(mask[point.X, point.Y]))
                expand.Add(point);
        }
    }
    
    #if DEBUG
    Debug.WriteLine(expand.Count + " pixels in the expand list.");
    Debug.WriteLine(next.Count + " pixels in the next list.");
    Debug.WriteLine("Highest blend-list count was " + HIGHEST_BLEND_COUNT + ".");
    if (RENDER_DEBUG)
    {
        for (int y = 0; y < h; y++)
        {
            ColorBgra* dst_ptr = dst.GetPointPointerUnchecked(0, y);
            for (int x = 0; x < w; x++)
            {
                if (mask[x,y])
                    pix = ColorBgra.White;
                else
                    pix = ColorBgra.Black;
                
                *dst_ptr = pix;
                dst_ptr++;
            }
        }
        // Tint pixels in expand-list red. Note that masked pixels might erroneously be included too!
        pix = ColorBgra.Red;
        foreach (Point p in expand)
        {
            dst[p.X, p.Y] = ColorBgra.Blend(pix, dst[p.X, p.Y], (byte)64);
        }
        // Tint pixels in next-list green.
        pix = ColorBgra.Green;
        foreach (Point p in next)
        {
            dst[p.X, p.Y] = ColorBgra.Blend(pix, dst[p.X, p.Y], (byte)128);
        }
        return;
    }
    #endif

    #if DEBUG
    Debug.WriteLine("Finished PreRender.");
    #endif
}


unsafe void Render(Surface dst, Surface src, Rectangle rect)
{
}

