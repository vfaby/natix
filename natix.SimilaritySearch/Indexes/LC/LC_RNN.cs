//
//   Copyright 2012 Eric Sadit Tellez <sadit@dep.fie.umich.mx>
//
//   Licensed under the Apache License, Version 2.0 (the "License");
//   you may not use this file except in compliance with the License.
//   You may obtain a copy of the License at
//
//       http://www.apache.org/licenses/LICENSE-2.0
//
//   Unless required by applicable law or agreed to in writing, software
//   distributed under the License is distributed on an "AS IS" BASIS,
//   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//   See the License for the specific language governing permissions and
//   limitations under the License.
//
//   Original filename: natix/SimilaritySearch/Indexes/LC_RNN.cs
// 
using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using NDesk.Options;
using natix.CompactDS;
using natix.SortingSearching;

namespace natix.SimilaritySearch
{
	/// <summary>
	/// LC with a fixed number of centers
	/// </summary>
	/// <exception cref='ArgumentNullException'>
	/// Is thrown when an argument passed to a method is invalid because it is <see langword="null" /> .
	/// </exception>
	public class LC_RNN : BasicIndex
	{
		/// <summary>
		/// The centers ids
		/// </summary>
		public IList<int> CENTERS;
		/// <summary>
		/// All responses to cov()
		/// </summary>
		public IList<float> COV;

		// protected IList<IRankSelect> INVINDEX;
		/// <summary>
		/// The index represented as a sequence
		/// </summary>
		public IRankSelectSeq SEQ;

		public override void Load (BinaryReader Input)
		{
			base.Load (Input);
			int m = Input.ReadInt32();
			this.CENTERS = new int[m];
			this.COV = new float[m];
			// PrimitiveIO<int>.ReadFromFile(Input, m, this.CENTERS);
			PrimitiveIO<float>.ReadFromFile(Input, m, this.COV);
			this.SEQ = RankSelectSeqGenericIO.Load(Input);
			var L = new SortedListRSCache(this.SEQ.Unravel(this.SEQ.Sigma - 1));
			this.CENTERS = new List<int>(L);
		}

		public override void Save (BinaryWriter Output)
		{
			base.Save (Output);
			Output.Write(this.CENTERS.Count);
			// PrimitiveIO<int>.WriteVector(Output, this.CENTERS);
			PrimitiveIO<float>.WriteVector(Output, this.COV);
			RankSelectSeqGenericIO.Save(Output, this.SEQ);
		}

		/// <summary>
		/// Returns the SEQuence index
		/// </summary>
		public IRankSelectSeq GetSEQ ()
		{
			return this.SEQ;
		}
		
		/// <summary>
		/// Returns the table of all covering radii
		/// </summary>
		public IList<float> GetCOV ()
		{
			return this.COV;
		}

		/// <summary>
		/// Initializes a new instance
		/// </summary>
		public LC_RNN () : base()
		{	
			// this.SeqBuilder = SequenceBuilders.GetIISeq (BitmapBuilders.GetPlainSortedList ());
		}

		/// <summary>
		/// SearchNN method (only used at construction time)
		/// </summary>
		public virtual void BuildSearchNN (object u, out int nn_center, out double nn_dist)
		{
			int num_centers = this.CENTERS.Count;
			nn_center = 0;
			nn_dist = float.MaxValue;
			for (int center_id = 0; center_id < num_centers; center_id++) {
				var curr_dist = this.DB.Dist (u, this.DB [this.CENTERS [center_id]]);
				if (curr_dist < nn_dist) {
					nn_dist = curr_dist;
					nn_center = center_id;
				}
			}
		}
		
		public virtual void BuildInternal (BitStream32 IsCenter, int[] seq_lc, SequenceBuilder seq_builder)
		{
			int len = this.DB.Count;
			int pc = len / 100 + 1;
			for (int docid = 0; docid < len; docid++) {
				if (docid % pc == 0) {
                    Console.WriteLine ("docid {0} of {1}, advance {2:0.00}%, timestamp: {3}", docid, len, docid * 100.0 / len, DateTime.Now);
				}
				if (IsCenter [docid]) {
					seq_lc[docid] = this.CENTERS.Count;
					continue;
				}
				int nn_center;
				double nn_dist;
				this.BuildSearchNN (this.DB[docid], out nn_center, out nn_dist);
				seq_lc[docid] = nn_center;
				if (this.COV [nn_center] < nn_dist) {
					this.COV [nn_center] = (float)nn_dist;
				}
			}
            if (seq_builder == null) {
                seq_builder = SequenceBuilders.GetIISeq(BitmapBuilders.GetPlainSortedList());
            }
			this.SEQ = seq_builder(seq_lc, this.CENTERS.Count + 1);
		}
		
		/// <summary>
		/// Build the index
		/// </summary>
		public virtual void Build (MetricDB db, int num_centers, SequenceBuilder seq_builder = null)
		{
			this.DB = db;
			this.CENTERS = RandomSets.GetRandomSubSet (num_centers, this.DB.Count);
			Sorting.Sort<int> (this.CENTERS);
			BitStream32 IsCenter = new BitStream32 ();
			IsCenter.Write (false, db.Count);
			var seq = new int[db.Count];
			this.COV = new float[num_centers];
			for (int i = 0; i < num_centers; i++) {
				IsCenter [this.CENTERS [i]] = true;
				seq [this.CENTERS [i]] = this.CENTERS.Count;
			}
			this.BuildInternal (IsCenter, seq, seq_builder);
			//this.Save (output_name, invindex);
		}

		public virtual void Build (LC_RNN lc, SequenceBuilder seq_builder = null)
		{
			this.COV = lc.COV;
			this.DB = lc.DB;
			this.CENTERS = new List<int>(lc.CENTERS);
            var S = lc.SEQ.GetRawSeq();
			if (seq_builder == null) {
				seq_builder = SequenceBuilders.GetIISeq(BitmapBuilders.GetPlainSortedList());
			}
			this.SEQ = seq_builder(S, lc.SEQ.Sigma);
		}


        public object CreateQueryContext (object q)
        {
            return new Dictionary<int, double>();
        }

        public bool MustReviewItem (object q, int item, double radius, object _ctx)
        {
            var cache_dcq = _ctx as Dictionary<int, double>;
            var centerID = this.SEQ.Access (item);
            if (centerID == this.CENTERS.Count) {
                // Console.WriteLine ("Item is center");
                return true;
            }
            var cov = this.COV [centerID];
            double dcq;
            if (!cache_dcq.TryGetValue (centerID, out dcq)) {
                var centerID_real = this.CENTERS[ centerID ];
                dcq = this.DB.Dist (this.DB[ centerID_real ], q);
                ++this.internal_numdists;
                cache_dcq[ centerID ] = dcq;
            }
            var review = ( dcq <= radius + cov);
            //Console.WriteLine ("internal_lc review: {0}, dqc: {1}, radius: {2}, cov: {3}", review, dcq, radius, cov);
            return review;
        }
 
		/// <summary>
		/// Search the specified q with radius qrad.
		/// </summary>
		public override IResult SearchRange (object q, double qrad)
		{
			var sp = this.DB;
			var R = sp.CreateResult (int.MaxValue, false);
			int len = this.CENTERS.Count;
			for (int center_id = 0; center_id < len; center_id++) {
				var dcq = sp.Dist (this.DB [this.CENTERS [center_id]], q);
				if (dcq <= qrad) {
					R.Push (this.CENTERS [center_id], dcq);
				}
				if (dcq <= qrad + this.COV [center_id]) {
					var rs = this.SEQ.Unravel (center_id);
					var count1 = rs.Count1;
					for (int i = 1; i <= count1; i++) {
						var u = rs.Select1 (i);
						var r = sp.Dist (q, sp [u]);
						if (r <= qrad) {
							R.Push (u, r);
						}
					}
				}
			}
			return R;
		}
		
		/// <summary>
		/// KNN search.
		/// </summary>
		public override IResult SearchKNN (object q, int K, IResult R)
		{
			var sp = this.DB;
			int len = this.CENTERS.Count;
			var C = this.DB.CreateResult (len, false);
			for (int center = 0; center < len; center++) {
				var dcq = sp.Dist (this.DB [this.CENTERS [center]], q);
                ++this.internal_numdists;
				R.Push (this.CENTERS [center], dcq);
				//var rm = Math.Abs (dcq - this.COV [center]);
				if (dcq <= R.CoveringRadius + this.COV [center]) {
				// if (rm <= R.CoveringRadius) {
					 C.Push (center, dcq);
					// C.Push (center, rm);
				}
			}
			foreach (ResultPair pair in C) {
				var dcq = pair.dist;
				var center = pair.docid;
				if (dcq <= R.CoveringRadius + this.COV [center]) {
					var rs = this.SEQ.Unravel (center);
					var count1 = rs.Count1;
					for (int i = 1; i <= count1; i++) {
						var u = rs.Select1 (i);
						var r = sp.Dist (q, sp [u]);
						//if (r <= qr) { // already handled by R.Push
						R.Push (u, r);
					}
				}
			}
			return R;
		}

        // public virtual void PartialSearchKNN_Adaptive (object q, int K, IResult R, IDictionary<int,double> cache, TopK<IRankSelect> queue)
        public virtual void PartialSearchKNN_Adaptive (object q, int K, IResult R, IDictionary<int,double> cache, List<double> queue_dist, List<IRankSelect> queue_list)
        {
            var sp = this.DB;
            int numcenters = this.CENTERS.Count;
            for (int center = 0; center < numcenters; center++) {
                double dcq = -1;
                var oid = this.CENTERS [center];
                var cov = this.COV[center];
                if (!cache.TryGetValue (oid, out dcq)) {
                    ++this.internal_numdists;
                    dcq = sp.Dist (sp [oid], q);
                    cache [oid] = dcq;
                    R.Push (oid, dcq);
                }
                // it could be negative!, it's a mathematical
                // expression independent of the metric properties
                var dcq_cov = dcq - cov;
                // if (dcq <= R.CoveringRadius + this.COV [center]) {
                if (dcq_cov <= R.CoveringRadius) {
                    var list = this.SEQ.Unravel(center);
                    queue_dist.Add(dcq_cov);
                    queue_list.Add(list);
                }
            }
        }

		/// <summary>
		/// Partial radius search
		/// </summary>
        public virtual void PartialSearchRange (object q, double qrad, IResult R, IDictionary<int,double> cache, List<IRankSelect> queue_list)
		{
			var sp = this.DB;
			int len = this.CENTERS.Count;
			// IList<IRankSelect> output_list = new List<IRankSelect> ();
			for (int center_id = 0; center_id < len; center_id++) {
				double dcq = -1;
				var oid = this.CENTERS [center_id];
                if (!cache.TryGetValue (oid, out dcq)) {
                    ++this.internal_numdists;
                    dcq = sp.Dist (sp [oid], q);
                    cache [oid] = dcq;
                    if (dcq <= qrad) {
                        R.Push (oid, dcq);
                    }
				}
                var cov = this.COV[center_id];
				if (dcq <= qrad + cov) {
					// output_list.Add (this.invindex [center_id]);
					//output_list.Add (new SortedListRS (this.SEQ.Unravel (center_id)));
                    queue_list.Add (this.SEQ.Unravel (center_id));
				}
			}
		}
	}
}
