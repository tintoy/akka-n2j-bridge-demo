package io.tintoy.utilities.streaming

import akka.stream.stage._
import akka.util.ByteString
import scala.annotation.tailrec

/**
 * Helper functions for parsing Akka streams.
 * @note AF: The next step is to produce a similar processing stage that instead splits by looking for length prefixes.
 */
object StreamParsers {
  /**
   * Create a stream-processing stage that reorganises the stream's elements into segments based on a separator.
   * @param separator The separator (as a [[String]]).
   * @param maximumSegmentLength The maximum number of bytes that the stage can process between separators before it emits an error.
   * @return
   */
  def split(separator: String, maximumSegmentLength: Int): StatefulStage[ByteString, String] = new StringSplitStage(separator, maximumSegmentLength)

  /**
   * A stream-processing stage that reorganises the stream's elements into segments based on a string separator.
   * @param separator The separator (as a [[String]]).
   * @param maximumSegmentLength The maximum number of bytes that the stage can process between separators before it emits an error.
   */
  private class StringSplitStage(separator: String, maximumSegmentLength: Int)
      extends StatefulStage[ByteString, String] {

    /**
     * A byte-level (UTF-8) representation of the separator.
     */
    private[this] val separatorBytes = ByteString(separator, ByteString.UTF_8)

    /**
     * The first byte of the separator (simplifies scanning).
     */
    private[this] val firstSeparatorByte = separatorBytes.head

    /**
     * The currently-accumulated bytes.
     */
    private[this] var buffer = ByteString.empty

    /**
     * The index of the next potential match (of the separator) to examine.
     */
    private[this] var nextPotentialMatchIndex = 0

    /**
     * The initial behaviour for the [[StringSplitStage]].
     */
    override def initial = new State {
      /**
       * Called when an element ([[ByteString]]) is pushed through the stream and arrives the [[StringSplitStage]] stage.
       * @param bytes The bytes to process.
       * @param context The current stage processing context.
       * @return A directive indicating the next action to be taken (emit or fail).
       */
      override def onPush(bytes: ByteString, context: Context[String]): SyncDirective = {
        buffer ++= bytes

        if (buffer.size > maximumSegmentLength) {
          context.fail(
            new IllegalStateException(
              s"Read $maximumSegmentLength without encountering the separator sequence."
            )
          )
        }
        else {
          emit(
            readSegment(Vector.empty).iterator,
            context
          )
        }
      }

      /**
       * Read a segment from the current element ([[ByteString]]) of the input stream.
       * @param existingSegments Existing segments (if any) that have been read from the current element.
       * @return All segments that have been read from the current element.
       */
      @tailrec
      private def readSegment(existingSegments: Vector[String]): Vector[String] = {
        val potentialMatchIndex = buffer.indexOf(firstSeparatorByte)
        if (potentialMatchIndex != -1) {
          // Found a potential match...
          if (potentialMatchIndex + separatorBytes.size > buffer.size) {
            // ...but we're at the end of this input element (ByteString) so we can't complete the match; remember position for next time
            nextPotentialMatchIndex = potentialMatchIndex
            existingSegments
          }
          else {
            // Try to match the rest of the separator
            val matchSlice: ByteString = buffer.slice(
              potentialMatchIndex,
              potentialMatchIndex + separatorBytes.size
            )

            if (matchSlice == separatorBytes) {
              // We've got a definite match
              val splitSegment = buffer.slice(0, potentialMatchIndex).utf8String
              val offset = potentialMatchIndex + separatorBytes.size // Remove separator, too
              buffer = buffer.drop(offset)
              nextPotentialMatchIndex -= offset

              readSegment(existingSegments :+ splitSegment)
            }
            else {
              // Nope, false alarm
              nextPotentialMatchIndex += 1
              readSegment(existingSegments)
            }
          }
        }
        else {
          // No match
          nextPotentialMatchIndex = buffer.size
          existingSegments
        }
      }
    }
  }
}
