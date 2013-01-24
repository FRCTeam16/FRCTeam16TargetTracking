using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Emgu.CV.Structure;
using Emgu.CV;
using System.Threading.Tasks;

namespace FRCTeam16TargetTracking
{
    public static class ImageFilter
    {
        public static List<MCvBox2D> FilterContours(Image<Gray, Byte> cannyEdges, double minArea, double approxPoly)
        {
            List<MCvBox2D> filteredContours = new List<MCvBox2D>();

            using (MemStorage storage = new MemStorage())
            {
               for (Contour<System.Drawing.Point> contours = cannyEdges.FindContours(Emgu.CV.CvEnum.CHAIN_APPROX_METHOD.CV_CHAIN_APPROX_SIMPLE, Emgu.CV.CvEnum.RETR_TYPE.CV_RETR_LIST, storage); contours != null; contours = contours.HNext) 
                {
                    if (contours != null)
                    {
                        Contour<System.Drawing.Point> currentContour = contours.ApproxPoly(approxPoly, storage);
                        if (currentContour.Total == 4 && currentContour.Convex && currentContour.Area > minArea)
                        {
                            if (currentContour.BoundingRectangle.Width > currentContour.BoundingRectangle.Height)
                            {
                                bool isRectangle = true;
                                System.Drawing.Point[] pts = currentContour.ToArray();
                                LineSegment2D[] edges = Emgu.CV.PointCollection.PolyLine(pts, true);

                                for (int i = 0; i < edges.Length; i++)
                                {
                                    double angle = Math.Abs(edges[(i + 1) % edges.Length].GetExteriorAngleDegree(edges[i]));

                                    if (angle < 85 || angle > 95)
                                    {
                                        isRectangle = false;
                                        break;
                                    }
                                }
                                if (isRectangle)
                                {
                                    filteredContours.Add(currentContour.GetMinAreaRect());
                                }
                            }
                        }
                    }
                }
            }

            if (filteredContours.Count > 0)
            {
                //filter out boxes with similar centers close by
                filteredContours = FilterSimilar(filteredContours);

                //sort highest to top
                filteredContours = SortAscending(filteredContours);
            }

            return filteredContours;
        }

        private static List<MCvBox2D> SortAscending(List<MCvBox2D> boxes)
        {
            return boxes.OrderBy(e=>e.center.Y).ToList();
        }

        private static List<MCvBox2D> FilterSimilar(List<MCvBox2D> boxes)
        {
            List<MCvBox2D> removalQueue = new List<MCvBox2D>();
            List<MCvBox2D> ret = new List<MCvBox2D>();
            ret = boxes;

            //Parallel.ForEach(boxes, b =>
            foreach(MCvBox2D b in boxes)
            {
                List<MCvBox2D> similar = GetSimilar(boxes, b);

                MCvBox2D largest = GetLargest(similar);

                similar.Remove(largest);

                removalQueue.AddRange(similar);
            }

            //Parallel.ForEach(removalQueue, rq =>
            foreach(MCvBox2D rq in removalQueue)
            {
                RemoveAll(rq, ret);
            }

            return ret;
        }

        private static List<MCvBox2D> GetSimilar(List<MCvBox2D> boxes, MCvBox2D box)
        {
            List<MCvBox2D> ret = new List<MCvBox2D>();
            int xCenter = (int)box.center.X;
            int yCenter = (int)box.center.Y;

            //Parallel.ForEach(boxes, b =>
            foreach(MCvBox2D b in boxes)
            {
                int bcx = (int)b.center.X;
                int bcy = (int)b.center.Y;

                int dx = Math.Abs(bcx - xCenter);
                int dy = Math.Abs(bcy - yCenter);

                int distSq = (dx * dx) + (dy * dy);

                if (distSq < 20 * 20)
                {
                    ret.Add(b);
                }
            }

            return ret;
        }

        private static MCvBox2D GetLargest(List<MCvBox2D> boxes)
        {
            MCvBox2D ret = new MCvBox2D();
            int i = 0;
            foreach(MCvBox2D b in boxes)
            {
                if (i == 0)
                {
                    ret = b;
                }

                if ((b.size.Height * b.size.Width) > (ret.size.Height * ret.size.Width))
                {
                    ret = b;
                }

                i++;
            }

            return ret;
        }

        private static void RemoveAll(MCvBox2D box, List<MCvBox2D> boxes) 
        {
            while (boxes.Contains(box))
            {
                boxes.Remove(box);
            }
        }
    }
}
